using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Extensions;

namespace RecursiveDataAnnotationsValidation
{
    /// <summary>Recursive validator for DataAnnotation attribute-based validation.</summary>
    public class RecursiveDataAnnotationValidator : IRecursiveDataAnnotationValidator, IAsyncRecursiveDataAnnotationValidator
    {
        //The deepest level that is validated. The depth of an object is the number of segments in its
        //shortest path: each property step and each collection index is one level, and the root is level 0.
        //An object at a deeper level is not validated and fails the validation (see GraphWalk.Walk).
        //The limit stops a computed property that builds one new object on each read, which never ends
        //otherwise. It does not stop one that builds two or more, because the walk runs out of memory first.
        //128 is twice the depth that System.Text.Json allows by default, which counts nearly the same way.
        internal const int MaxDepth = 128;

        /// <summary>Runs validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationContext">Validation context.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur. Can be null when only the return value is needed.</param>
        /// <returns>Returns true if all validation passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="validationContext"/> is null.</exception>
        public bool TryValidateObjectRecursive(
            object obj,  // see Note 1 
            ValidationContext validationContext, 
            List<ValidationResult> validationResults
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (validationContext == null) throw new ArgumentNullException(nameof(validationContext));

            return TryValidateGraph(
                obj,
                validationResults,
                validationContext,
                validationContext.Items
            );
        }

        /// <summary>Runs validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur. Can be null when only the return value is needed.</param>
        /// <param name="validationContextItems">Validation context items.</param>
        /// <returns>Returns true if all validation passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
        public bool TryValidateObjectRecursive(
            object obj,
            List<ValidationResult> validationResults,
            IDictionary<object, object> validationContextItems = null
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            return TryValidateGraph(
                obj,
                validationResults,
                null,
                validationContextItems
            );
        }

        /// <summary>Runs async validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationContext">Validation context.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur. Can be null when only the return value is needed.</param>
        /// <returns>Returns true if all validation passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="validationContext"/> is null.</exception>
        public async Task<bool> TryValidateObjectRecursiveAsync(
            object obj,
            ValidationContext validationContext,
            List<ValidationResult> validationResults
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (validationContext == null) throw new ArgumentNullException(nameof(validationContext));

            return await Task.Run(() => TryValidateGraph(
                obj,
                validationResults,
                validationContext,
                validationContext.Items
            ));
        }

        /// <summary>Runs async validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur. Can be null when only the return value is needed.</param>
        /// <param name="validationContextItems">Validation context items.</param>
        /// <returns>Returns true if all validation passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
        public async Task<bool> TryValidateObjectRecursiveAsync(
            object obj,
            List<ValidationResult> validationResults,
            IDictionary<object, object> validationContextItems = null
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            return await Task.Run(() => TryValidateGraph(
                obj,
                validationResults,
                null,
                validationContextItems
            ));
        }

        //serviceProvider is the caller's ValidationContext, or null when the caller passed only items.
        //Validator passes the outer context as the service provider of each context it builds, so
        //GetService on a context built here reaches the caller's provider the same way.
        private static bool TryValidateGraph(
            object obj,
            List<ValidationResult> validationResults,
            IServiceProvider serviceProvider,
            IDictionary<object, object> validationContextItems
            )
        {
            //like Validator.TryValidateObject, a null list means the caller wants only the return value
            validationResults = validationResults ?? new List<ValidationResult>();

            return new GraphWalk(validationResults, serviceProvider, validationContextItems).Run(obj);
        }

        //One walk over an object graph, breadth first. The walk keeps its work in a queue and calls
        //nothing recursively, so the depth of a graph does not use the stack.
        //
        //A level is one segment of a path: a property or an index. Each step from an object to the
        //next one is one level, so the queue holds the levels in order, and the first path that
        //reaches an object is the shortest one. The walk marks an object when it queues it, and
        //drops every later path to it. The depth of an object is therefore true.
        //
        //A property that holds a collection is a step of its own: the collection waits in the queue
        //one level below its object, and its items are queued one level below that. Without it, an
        //item would be two levels away from its object and the queue would not be in order.
        private sealed class GraphWalk
        {
            private readonly Queue<WorkItem> queue = new Queue<WorkItem>();

            //every object that was queued, compared by reference
            private readonly HashSet<object> queuedObjects = new HashSet<object>(ObjectReferenceComparer.Instance);

            //every object that was validated but not walked, because it Equals an ancestor on its path
            private readonly HashSet<object> stoppedObjects = new HashSet<object>(ObjectReferenceComparer.Instance);

            //every collection that was enumerated, compared by reference. A collection that a second
            //route reaches is enumerated again, so an item that was added in the meantime, such as by a
            //Validate method, is found. Its struct items are skipped the second time: a struct has no
            //identity of its own, so queuedObjects cannot tell that the copy is the same struct, and it
            //would be reported once for each route. Its class items are dropped by queuedObjects.
            private readonly HashSet<object> enumeratedCollections = new HashSet<object>(ObjectReferenceComparer.Instance);

            private readonly ICollection<ValidationResult> validationResults;
            private readonly IServiceProvider serviceProvider;
            private readonly IDictionary<object, object> validationContextItems;

            public GraphWalk(
                ICollection<ValidationResult> validationResults,
                IServiceProvider serviceProvider,
                IDictionary<object, object> validationContextItems
                )
            {
                this.validationResults = validationResults;
                this.serviceProvider = serviceProvider;
                this.validationContextItems = validationContextItems;
            }

            public bool Run(object root)
            {
                //an object of a leaf type can never produce a result (see IsLeafType)
                if (root.GetType().IsLeafType())
                {
                    return true;
                }

                queuedObjects.Add(root);
                queue.Enqueue(new WorkItem(root, null, 0, null, false, false));

                var valid = true;
                while (queue.Count > 0)
                {
                    var item = queue.Dequeue();
                    if (item.IsCollectionOfProperty)
                    {
                        EnqueueItems((IEnumerable)item.Value, item.Path, item.Depth + 1, item.Ancestors);
                    }
                    else if (!Walk(item))
                    {
                        valid = false;
                    }
                }

                return valid;
            }

            //Queues an object that was found at the end of a path. The path is the parent path plus a
            //property name, or plus an index if the name is null. A path is a chain of steps, and it is
            //turned into a string only for an object that has a result.
            private void Enqueue(
                object value,
                PathStep parent,
                string propertyName,
                int index,
                int depth,
                EqualsAncestor ancestors
                )
            {
                //an object of a leaf type can never produce a result, such as a boxed int in an object[] (see IsLeafType)
                //An object that was queued before is skipped, which also ends a cycle in the graph.
                var type = value.GetType();
                if (type.IsLeafType() || queuedObjects.Contains(value))
                {
                    return;
                }

                //An object that Equals an ancestor on this path is validated, but not walked from here.
                //It is not marked, so a later path that does not pass an equal ancestor can still walk it.
                var stopHere = type.OverridesEquals() && EqualsAnAncestor(value, type, ancestors);
                if (!stopHere)
                {
                    queuedObjects.Add(value);
                }

                queue.Enqueue(new WorkItem(
                    value,
                    new PathStep(parent, propertyName, index),
                    depth,
                    ancestors,
                    false,
                    stopHere
                    ));
            }

            //Queues each item of a collection. The path of an item is the path of the collection
            //plus its index, and its depth is itemDepth. A collection that was enumerated before skips
            //its struct items (see enumeratedCollections).
            private void EnqueueItems(
                IEnumerable items,
                PathStep collectionPath,
                int itemDepth,
                EqualsAncestor ancestors
                )
            {
                var enumeratedBefore = !enumeratedCollections.Add(items);

                var arrayIndex = -1;
                foreach (var item in items)
                {
                    arrayIndex++;

                    //NOTE: Possibly should have a separate case for Dictionary which reports on the key

                    if (item == null) continue;
                    if (enumeratedBefore && item.GetType().IsValueType) continue;
                    Enqueue(item, collectionPath, null, arrayIndex, itemDepth, ancestors);
                }
            }

            //Walks an object: validates it, then queues what it holds. Returns false if the object has a result.
            private bool Walk(WorkItem item)
            {
                var obj = item.Value;
                var type = obj.GetType();

                //a computed property can return a new, equal object on each read, such as
                //`Money Zero => new Money(0)`, so references never repeat and the walk would not end.
                //Stop at an object that Equals an object on its own path: validate its own attributes,
                //so a child that Equals its parent by Id is still checked, but don't walk into it.
                //(the check runs when the object is queued, see Enqueue)
                if (item.EqualsAnAncestor)
                {
                    //Validated once, however many paths stop at it. A path that walks it was queued
                    //after this one (Enqueue drops a stop for an object that is queued), so it comes out
                    //of the queue later, and it skips the validation (see below).
                    return !stoppedObjects.Add(obj) || Validate(item);
                }

                var overridesEquals = type.OverridesEquals();

                //An object this deep is not validated and not walked, and the validation fails:
                //nobody has checked it, so it must not pass. The path to it is the shortest one,
                //so the depth is true, and the object gets one error, and not one for each path.
                //A leaf and an object queued before never get here (see Enqueue), and an object that
                //Equals an ancestor on its path returned above, so none of them gets a depth error.
                if (item.Depth > MaxDepth)
                {
                    validationResults.Add(new ValidationResult(
                        "The object is nested more than " + MaxDepth + " levels deep and was not validated.",
                        new[] { item.Path.ToString() }
                        ));
                    return false;
                }

                var ancestors = overridesEquals ? new EqualsAncestor(obj, item.Ancestors) : item.Ancestors;

                //an object that a shorter path stopped at was validated there, so only walk it now
                var result = (stoppedObjects.Count > 0 && stoppedObjects.Contains(obj)) || Validate(item);

                //A collection that gets here is the root object or an item of another collection. It is
                //validated as an object above, so its own attributes run. Then its items are queued,
                //before its properties. A collection that a property holds never gets here: it waits in
                //the queue as a step of its own (see below).
                //A collection of leaf types is skipped, like a collection that a property holds. A default
                //struct, such as an ImmutableArray nobody set, is skipped: it holds nothing and enumerating it throws.
                if (obj is IEnumerable items
                    && !type.IsCollectionOfLeafType()
                    && !obj.IsDefaultStruct())
                {
                    EnqueueItems(items, item.Path, item.Depth + 1, ancestors);
                }

                //GetWalkedProperties leaves out properties declared by framework types that throw or never
                //end when read, such as Uri.Segments on a relative Uri, DirectoryInfo.Root, or the properties
                //of a Thread or Process read from the wrong thread or process (see IsUnsafeToWalk). For a
                //collection, it also leaves out the properties that framework types declare, such as
                //Array.SyncRoot or LinkedList.First, because they repeat the items that were just queued.
                var properties = type.GetWalkedProperties();

                foreach (var property in properties)
                {
                    var value = property.GetValue(obj, null);

                    switch (value)
                    {
                        case null:
                            continue;

                        //items of a leaf type can never produce a result, so don't enumerate them (see IsLeafType)
                        case IEnumerable _ when value.GetType().IsCollectionOfLeafType():
                            continue;

                        //a struct collection nobody set, such as a default ImmutableArray, holds nothing
                        //and enumerating it throws, so skip it as an item is skipped. Only for a property
                        //declared as a struct: a property declared as an interface or object was always
                        //enumerated, and a boxed struct in one must not start to be skipped.
                        case IEnumerable _ when property.PropertyType.IsValueType && value.IsDefaultStruct():
                            continue;

                        case IEnumerable asEnumerable:
                            //the property is one level and the index of each item is another
                            queue.Enqueue(new WorkItem(
                                asEnumerable,
                                new PathStep(item.Path, property.Name, -1),
                                item.Depth + 1,
                                ancestors,
                                true,
                                false
                                ));
                            break;

                        default:
                            Enqueue(value, item.Path, property.Name, -1, item.Depth + 1, ancestors);
                            break;
                    }
                }

                return result;
            }

            //Validates the attributes and IValidatableObject of one object. A result of the root
            //object goes to the caller's list as it is. A result of any other object gets its member
            //names prefixed by the path of the object, so the names are the full path from the root.
            //The root object has no path, so its results keep their member names. A result that has no
            //member names, or a null or empty one, is an error of the whole object, such as one from a
            //class-level attribute, so it gets the path of the object itself (see MemberNames).
            private bool Validate(WorkItem item)
            {
                if (item.Path == null)
                {
                    return TryValidateObject(item.Value, validationResults);
                }

                var results = new List<ValidationResult>();
                if (TryValidateObject(item.Value, results))
                {
                    return true;
                }

                var path = item.Path.ToString();
                foreach (var validationResult in results)
                {
                    validationResults.Add(new ValidationResult(validationResult.ErrorMessage, MemberNames(validationResult, path)));
                }

                return false;
            }

            //The member names of a nested result, as paths from the root. A null or empty name means
            //the object itself, so it becomes the path alone ("Lines[1]"), and not the path with a dot
            //and nothing after it. A result with no names at all gets the path as its only name.
            private static List<string> MemberNames(ValidationResult validationResult, string path)
            {
                var memberNames = validationResult.MemberNames
                    .Select(x => string.IsNullOrEmpty(x) ? path : path + "." + x)
                    .ToList();

                if (memberNames.Count == 0)
                {
                    memberNames.Add(path);
                }

                return memberNames;
            }

            private bool TryValidateObject(object obj, ICollection<ValidationResult> results)
            {
                return Validator.TryValidateObject(
                    obj,
                    new ValidationContext(
                        obj,
                        serviceProvider,
                        validationContextItems
                    ),
                    results,
                    true
                );
            }

            //True when obj Equals an object on the path whose type is obj's type, a base of it, or derived
            //from it. Related types cover a computed property that alternates between a type and its
            //subclass. Unrelated types are not compared, so an Equals that casts without a type check
            //does not throw.
            private static bool EqualsAnAncestor(object obj, Type type, EqualsAncestor ancestors)
            {
                for (var ancestor = ancestors; ancestor != null; ancestor = ancestor.Parent)
                {
                    var ancestorType = ancestor.Value.GetType();
                    if ((ancestorType.IsAssignableFrom(type) || type.IsAssignableFrom(ancestorType))
                        && obj.Equals(ancestor.Value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        //An object that waits in the queue, and how the walk got to it.
        //Path is null for the object that the caller passed in.
        //IsCollectionOfProperty is true for the collection that a property holds. It is not validated
        //as an object. Its items are queued when it comes out of the queue.
        //Ancestors are the objects on the path whose type overrides Equals, the nearest one first.
        private sealed class WorkItem
        {
            public WorkItem(
                object value,
                PathStep path,
                int depth,
                EqualsAncestor ancestors,
                bool isCollectionOfProperty,
                bool equalsAnAncestor
                )
            {
                EqualsAnAncestor = equalsAnAncestor;
                Value = value;
                Path = path;
                Depth = depth;
                Ancestors = ancestors;
                IsCollectionOfProperty = isCollectionOfProperty;
            }

            public object Value { get; }
            public PathStep Path { get; }
            public int Depth { get; }
            public EqualsAncestor Ancestors { get; }
            public bool IsCollectionOfProperty { get; }
            public bool EqualsAnAncestor { get; }
        }

        //The last segment of a path and a link to the rest of it: a property name, or an index if
        //the name is null. Many objects share the first part of their paths, so the string of a
        //path is built only when an object has a result.
        private sealed class PathStep
        {
            private readonly PathStep parent;
            private readonly string propertyName;
            private readonly int index;

            public PathStep(PathStep parent, string propertyName, int index)
            {
                this.parent = parent;
                this.propertyName = propertyName;
                this.index = index;
            }

            //"Orders[0].Lines[1].Product": a name after the first one is joined with a dot, and an index with none
            public override string ToString()
            {
                var steps = new List<PathStep>();
                for (var step = this; step != null; step = step.parent)
                {
                    steps.Add(step);
                }

                var path = new StringBuilder();
                for (var i = steps.Count - 1; i >= 0; i--)
                {
                    var step = steps[i];
                    if (step.propertyName == null)
                    {
                        path.Append('[').Append(step.index).Append(']');
                        continue;
                    }

                    if (path.Length > 0)
                    {
                        path.Append('.');
                    }

                    path.Append(step.propertyName);
                }

                return path.ToString();
            }
        }

        //A link in the chain of the objects on a path whose type overrides Equals. The chain
        //starts at the nearest one, so an object can be compared with its ancestors only.
        private sealed class EqualsAncestor
        {
            public EqualsAncestor(object value, EqualsAncestor parent)
            {
                Value = value;
                Parent = parent;
            }

            public object Value { get; }
            public EqualsAncestor Parent { get; }
        }

        /* Note 1:
         *
         * Background information of why we don't use ValidationContext.ObjectInstance here, even though it is tempting.
         *
         * https://jeffhandley.com/2009-10-17/validator
         *
         * It’s important to note that for cross-field validation, relying on the ObjectInstance comes with a caveat.
         * It’s possible that the end user has entered a value for a property that could not be set—for instance
         * specifying “ABC” for a numeric field.  In cases like that, asking the instance for that numeric property
         * will of course not give you the “ABC” value that the user has entered, thus the object’s other properties
         * are in an indeterminate state.  But even so, we’ve found that it’s extremely valuable to provide this object
         * instance to the validation attributes.
         *
         * See also:
         * 
         * https://github.com/dotnet/corefx/blob/8b04d0a18a49448ff7c8ee63239cd6d2a2be7e14/src/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationContext.cs
         * 
         */
    }
}