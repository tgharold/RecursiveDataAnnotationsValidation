using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Extensions;

namespace RecursiveDataAnnotationsValidation
{
    /// <summary>Recursive validator for DataAnnotation attribute-based validation.</summary>
    public class RecursiveDataAnnotationValidator : IRecursiveDataAnnotationValidator, IAsyncRecursiveDataAnnotationValidator
    {
        //The deepest level that is validated. The depth of an object is the number of segments in its
        //path: each property step and each collection index is one level, and the root is level 0.
        //An object at a deeper level is not validated and fails the validation (see DepthExceededResult).
        //The walk calls itself once for each level, and a StackOverflowException cannot be caught.
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
        private bool TryValidateGraph(
            object obj,
            List<ValidationResult> validationResults,
            IServiceProvider serviceProvider,
            IDictionary<object, object> validationContextItems
            )
        {
            //like Validator.TryValidateObject, a null list means the caller wants only the return value
            validationResults = validationResults ?? new List<ValidationResult>();

            return TryValidateObjectRecursive(
                obj,
                validationResults,
                new HashSet<object>(ObjectReferenceComparer.Instance),
                new List<object>(),
                serviceProvider,
                validationContextItems,
                0
                );
        }

        /// <summary>
        /// Validates the specified object and adds any validation results to the provided collection.
        /// </summary>
        /// <param name="obj">The object to validate.</param>
        /// <param name="validationResults">A collection to receive any validation errors.</param>
        /// <param name="serviceProvider">Service provider for the validation context, or null.</param>
        /// <param name="validationContextItems">Optional context items for the validation context.</param>
        /// <returns>True if the object is valid; otherwise, false.</returns>
        private bool TryValidateObject(
            object obj, 
            ICollection<ValidationResult> validationResults, 
            IServiceProvider serviceProvider,
            IDictionary<object, object> validationContextItems
            )
        {
            return Validator.TryValidateObject(
                obj, 
                new ValidationContext(
                    obj, 
                    serviceProvider,
                    validationContextItems
                ), 
                validationResults, 
                true
            );
        }

        //True when obj Equals an object on the path whose type is obj's type, a base of it, or derived
        //from it. Related types cover a computed property that alternates between a type and its
        //subclass. Unrelated types are not compared, so an Equals that casts without a type check
        //does not throw.
        private static bool EqualsAnAncestor(object obj, Type type, List<object> equalityPath)
        {
            foreach (var ancestor in equalityPath)
            {
                var ancestorType = ancestor.GetType();
                if ((ancestorType.IsAssignableFrom(type) || type.IsAssignableFrom(ancestorType))
                    && obj.Equals(ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        //validatedObjects holds every object visited so far, compared by reference.
        //equalityPath holds the objects on the path from the root to this object whose type overrides Equals.
        //depth is the number of segments in the path of obj, which is 0 for the root object.
        //enumerateItems is true for an item of a collection. If the item is itself a collection, its
        //items are validated too. A collection that a property holds is enumerated by the caller, and
        //the root object is never enumerated.
        private bool TryValidateObjectRecursive(
            object obj,
            ICollection<ValidationResult> validationResults,
            ISet<object> validatedObjects,
            List<object> equalityPath,
            IServiceProvider serviceProvider,
            IDictionary<object, object> validationContextItems,
            int depth,
            bool enumerateItems = false
            )
        {
            var type = obj.GetType();

            //an object of a leaf type can never produce a result, such as a boxed int in an object[] (see IsLeafType)
            if (type.IsLeafType())
            {
                return true;
            }

            //short-circuit to avoid infinite loops on cyclical object graphs
            if (validatedObjects.Contains(obj))
            {
                return true;
            }

            //a computed property can return a new, equal object on each read, such as
            //`Money Zero => new Money(0)`, so references never repeat and the walk would not end.
            //Stop at an object that Equals an object on its own path: validate its own attributes,
            //so a child that Equals its parent by Id is still checked, but don't walk into it.
            var overridesEquals = type.OverridesEquals();
            if (overridesEquals && EqualsAnAncestor(obj, type, equalityPath))
            {
                validatedObjects.Add(obj);
                return TryValidateObject(obj, validationResults, serviceProvider, validationContextItems);
            }

            //an object this deep is not validated and not walked, and the validation fails: nobody
            //has checked it, so it must not pass. A leaf, an object seen before and an object that
            //Equals an ancestor all returned above, because none of them walks any further.
            if (depth > MaxDepth)
            {
                validationResults.Add(new DepthExceededResult());
                return false;
            }

            validatedObjects.Add(obj);
            if (overridesEquals) equalityPath.Add(obj);

            var result = TryValidateObject(obj, validationResults, serviceProvider, validationContextItems);

            //An item that is a collection is validated as an object above, so its own attributes run.
            //Then its items are validated, before its properties are walked. An object that the
            //collection also returns from a property, such as Array.SyncRoot, is then reported at its
            //index (Value[0][0]) and not through the property (Value[0].SyncRoot[0]).
            //A collection of leaf types is skipped, like a collection that a property holds. A default
            //struct, such as an ImmutableArray nobody set, is skipped: it holds nothing and enumerating it throws.
            var enumeratedItems = false;
            if (enumerateItems
                && obj is IEnumerable items
                && !type.IsCollectionOfLeafType()
                && !obj.IsDefaultStruct())
            {
                enumeratedItems = true;
                if (!TryValidateItems(
                    items,
                    "",
                    validationResults,
                    validatedObjects,
                    equalityPath,
                    serviceProvider,
                    validationContextItems,
                    depth
                    ))
                {
                    result = false;
                }
            }

            //IsWalked leaves out properties declared by framework types that throw or never end when
            //read, such as Uri.Segments on a relative Uri, DirectoryInfo.Root, or the properties of
            //a Thread or Process read from the wrong thread or process (see IsUnsafeToWalk)
            var properties = type.GetProperties().Where(prop => prop.IsWalked()).ToList();

            foreach (var property in properties)
            {
                var value = property.GetValue(obj, null);

                List<ValidationResult> nestedResults;
                switch (value)
                {
                    case null:
                        continue;

                    //items of a leaf type can never produce a result, so don't enumerate them (see IsLeafType)
                    case IEnumerable _ when value.GetType().IsCollectionOfLeafType():
                        continue;

                    //a struct collection nobody set, such as a default ImmutableArray, holds nothing
                    //and enumerating it throws, so skip it as an item is skipped
                    case IEnumerable _ when value.IsDefaultStruct():
                        continue;

                    case IEnumerable asEnumerable:
                        //an item that was enumerated above can return itself from a property, such as
                        //Array.SyncRoot, and enumerating it a second time would find nothing new
                        if (enumeratedItems && ReferenceEquals(value, obj)) continue;

                        //the property is one level and the index of each item is another
                        if (!TryValidateItems(
                            asEnumerable,
                            property.Name,
                            validationResults,
                            validatedObjects,
                            equalityPath,
                            serviceProvider,
                            validationContextItems,
                            depth + 1
                            ))
                        {
                            result = false;
                        }
                        break;

                    default:
                        nestedResults = new List<ValidationResult>();
                        if (!TryValidateObjectRecursive(
                            value, 
                            nestedResults, 
                            validatedObjects, 
                            equalityPath,
                            serviceProvider,
                            validationContextItems,
                            depth + 1
                            ))
                        {
                            result = false;
                            foreach (var validationResult in nestedResults)
                            {
                                var property1 = property;

                                //an object that is too deep has no member names, and its path is the property
                                var memberNames = validationResult is DepthExceededResult
                                    ? new[] { property1.Name }
                                    : validationResult.MemberNames.Select(x => property1.Name + '.' + x);
                                validationResults.Add(new ValidationResult(validationResult.ErrorMessage, memberNames));
                            }
                        }
                        break;
                }
            }

            if (overridesEquals) equalityPath.RemoveAt(equalityPath.Count - 1);

            return result;
        }
        
        //Validates each item of a collection. The result of an item is added to validationResults with
        //its member names prefixed by the property name, if there is one, and the index of the item.
        //collectionDepth is the depth of the collection, or of the property that holds it, so each
        //item is one level deeper.
        private bool TryValidateItems(
            IEnumerable items,
            string propertyName,
            ICollection<ValidationResult> validationResults,
            ISet<object> validatedObjects,
            List<object> equalityPath,
            IServiceProvider serviceProvider,
            IDictionary<object, object> validationContextItems,
            int collectionDepth
            )
        {
            var result = true;
            var arrayIndex = -1;
            foreach (var item in items)
            {
                arrayIndex++;

                //NOTE: Possibly should have a separate case for Dictionary which reports on the key

                if (item == null) continue;
                var nestedResults = new List<ValidationResult>();
                if (!TryValidateObjectRecursive(
                    item,
                    nestedResults,
                    validatedObjects,
                    equalityPath,
                    serviceProvider,
                    validationContextItems,
                    collectionDepth + 1,
                    enumerateItems: true
                    ))
                {
                    result = false;
                    var index = arrayIndex;
                    foreach (var validationResult in nestedResults)
                    {
                        //the member names of an item that is a collection start with the index of its own items
                        var startsWithIndex = validationResult is ItemOfCollectionResult;
                        //an item that is too deep has no member names, and its path is the index
                        var memberNames = validationResult is DepthExceededResult
                            ? new List<string> { propertyName + "[" + index + "]" }
                            : validationResult.MemberNames
                                .Select(x => propertyName + "[" + index + "]" + (startsWithIndex ? "" : ".") + x)
                                .ToList();

                        //With no property name, the result is for an item that is a collection: the
                        //caller puts its own index in front of these names, with no dot.
                        validationResults.Add(propertyName.Length == 0
                            ? new ItemOfCollectionResult(validationResult.ErrorMessage, memberNames)
                            : new ValidationResult(validationResult.ErrorMessage, memberNames));
                    }
                }
            }

            return result;
        }

        //The result for an object that is deeper than MaxDepth. It has no member names at first. The
        //caller that knows how it reached the object gives it that path as its only member name, and
        //every caller above it prefixes the name like any other, so the final name is the full path.
        private sealed class DepthExceededResult : ValidationResult
        {
            public DepthExceededResult()
                : base("The object is nested more than " + MaxDepth + " levels deep and was not validated.")
            {
            }
        }

        //A result whose member names start with the index of an item, such as "[0].Name", and not
        //with a property name. It marks the names that the caller must join without a dot.
        //The names the items choose are not looked at, so a name that starts with "[", or a null
        //name, is joined like any other name. A result of this type never reaches the caller,
        //because the object passed to the validator is not enumerated, and every other result
        //is rebuilt with a property name in front.
        private sealed class ItemOfCollectionResult : ValidationResult
        {
            public ItemOfCollectionResult(string errorMessage, IEnumerable<string> memberNames)
                : base(errorMessage, memberNames)
            {
            }
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