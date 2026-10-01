using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// Object shapes that are unusual, made up or taken from real framework types, to see what the
    /// validator does with them. The rule that the validator walks into every public property of a
    /// reference type, and enumerates every IEnumerable it finds in one, explains all results.
    /// A property is walked when it is readable, not an indexer, not marked
    /// [SkipRecursiveValidation], and of a reference type other than string. See IsWalked.
    /// Each test is one of:
    /// - A guard. It passes today and keeps a behavior that callers may rely on.
    /// - A limitation guard. It passes today and shows a shape that is silently not validated.
    ///   If the validator learns to walk that shape, the test fails on purpose, so the change is deliberate.
    /// - An open test. It is skipped and states the behavior a fix would give.
    /// Every result below is the same on release 2.2.0 and on the current code, except the
    /// framework types in MembersThatThrow, which are no longer walked.
    /// Not covered, because it stops the test run: a Task that has not completed makes the walk
    /// read Task.Result, which waits forever.
    /// </summary>
    public class OddShapeTests
    {
        public class Leaf
        {
            [Required]
            public string Name { get; set; }
        }

        public class Holder<T>
        {
            public T Value { get; set; }
        }

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

        private const string NameRequired = " | The Name field is required.";

        /// <summary>
        /// A collection inside a collection. The validator walks the properties of an item and
        /// enumerates a property that is a collection. It never enumerates an item that is itself a
        /// collection, because an item is not a property. So a List of List, or an object array that
        /// holds a List, hides the objects inside.
        /// A dictionary works, because its item is a KeyValuePair whose Value is a property.
        /// A jagged array works by accident: the item is an array, and an array has a public
        /// SyncRoot property that returns the array itself, which the validator walks and enumerates.
        /// See: https://learn.microsoft.com/dotnet/api/system.array.syncroot
        /// </summary>
        public class CollectionsInsideCollections
        {
            [Fact(Skip = "Not fixed yet. An item that is itself a collection is not enumerated, so its objects are not validated.")]
            public void List_of_lists_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<List<Leaf>>>
                {
                    Value = new List<List<Leaf>> { new List<Leaf> { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Single(errors);
            }

            [Fact(Skip = "Not fixed yet. An item that is itself a collection is not enumerated, so its objects are not validated.")]
            public void Object_array_holding_a_list_is_validated()
            {
                var (valid, errors) = Run(new Holder<object[]>
                {
                    Value = new object[] { new List<Leaf> { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Single(errors);
            }

            [Fact]
            public void Dictionary_of_lists_is_validated_through_the_pair_value()
            {
                var (valid, errors) = Run(new Holder<Dictionary<string, List<Leaf>>>
                {
                    Value = new Dictionary<string, List<Leaf>> { ["k"] = new List<Leaf> { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Value[0].Name" + NameRequired), errors);
            }

            // The path has SyncRoot in it. If nested collections are enumerated properly, the
            // path becomes "Value[0][0].Name" and this test fails on purpose.
            [Fact]
            public void Jagged_array_is_validated_through_SyncRoot()
            {
                var (valid, errors) = Run(new Holder<Leaf[][]>
                {
                    Value = new[] { new[] { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].SyncRoot[0].Name" + NameRequired), errors);
            }
        }

        /// <summary>
        /// A collection class that has members of its own, such as the paging data on a result list.
        /// The validator enumerates the collection, but never validates the collection object.
        /// Its own attributes do not run, and its own properties are not walked.
        /// The items are still validated.
        /// Whether the attributes of the collection object should run is open. Running them would
        /// fail models that pass today.
        /// </summary>
        public class CollectionsWithMembersOfTheirOwn
        {
            public class PagedList<T> : List<T>
            {
                [Required]
                public string Cursor { get; set; }
            }

            [Fact(Skip = "Open. The attributes on the collection object itself do not run, and whether they should is not decided.")]
            public void Attributes_on_the_collection_object_are_validated()
            {
                var (valid, errors) = Run(new Holder<PagedList<Leaf>> { Value = new PagedList<Leaf> { Cursor = null } });

                Assert.False(valid);
                Assert.Single(errors);
            }

            [Fact]
            public void Items_of_the_collection_are_validated()
            {
                var page = new PagedList<Leaf> { Cursor = "c" };
                page.Add(new Leaf());

                var (valid, errors) = Run(new Holder<PagedList<Leaf>> { Value = page });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
            }
        }

        /// <summary>
        /// Structs. The validator walks into properties of reference types only, so a struct
        /// property is checked for its own validation attributes by Validator when the parent is
        /// validated, but nothing inside the struct is walked. A property of the struct that
        /// carries an attribute is never checked, and neither are the items of a collection that is
        /// itself a struct, such as ImmutableArray&lt;T&gt;.
        /// These are limitation guards. Record structs with positional `[property: ...]`
        /// attributes are a modern way to model a value, so this one may surprise callers.
        /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/struct
        /// </summary>
        public class StructsAreNotWalked
        {
            public struct Money
            {
                [Range(1, 10)]
                public int Amount { get; set; }
            }

            public readonly record struct Coordinates([property: Range(-90, 90)] double Latitude);

            public readonly struct LeafBag : IEnumerable<Leaf>
            {
                private readonly Leaf[] _items;

                public LeafBag(params Leaf[] items)
                {
                    _items = items;
                }

                public IEnumerator<Leaf> GetEnumerator() => ((IEnumerable<Leaf>)_items).GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [Fact]
            public void Struct_property_with_an_attribute_is_not_validated()
            {
                var (valid, errors) = Run(new Holder<Money> { Value = new Money { Amount = 99 } });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Nullable_struct_property_with_an_attribute_is_not_validated()
            {
                var (valid, errors) = Run(new Holder<Money?> { Value = new Money { Amount = 99 } });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Record_struct_property_with_an_attribute_is_not_validated()
            {
                var (valid, errors) = Run(new Holder<Coordinates> { Value = new Coordinates(200) });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Struct_collection_items_are_not_validated()
            {
                var (valid, errors) = Run(new Holder<LeafBag> { Value = new LeafBag(new Leaf()) });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Struct_that_is_the_root_object_is_validated()
            {
                // The root is passed straight to Validator, so its own attributes do run.
                var (valid, errors) = Run(new Money { Amount = 99 });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Amount | The field Amount must be between 1 and 10."), errors);
            }
        }

        /// <summary>
        /// Positional records. `record R([Required] string Name)` puts the attribute on the
        /// constructor parameter, not on the generated property. Validator only reads property
        /// attributes, so nothing runs. `[property: Required]` puts it on the property.
        /// This comes from the framework's Validator, not from this library. The configuration
        /// settings of the consumers found use the `[property: ...]` form.
        /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record#positional-syntax-for-property-definition
        /// </summary>
        public class PositionalRecords
        {
            public sealed record ParameterAttribute([Required] string Name);

            public sealed record PropertyAttribute([property: Required] string Name);

            [Fact]
            public void Attribute_on_the_parameter_is_not_validated()
            {
                var (valid, errors) = Run(new Holder<ParameterAttribute> { Value = new ParameterAttribute(null) });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Attribute_with_the_property_target_is_validated()
            {
                var (valid, errors) = Run(new Holder<PropertyAttribute> { Value = new PropertyAttribute(null) });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value.Name" + NameRequired), errors);
            }
        }

        /// <summary>
        /// Members that the validator reads, with effects a model author may not expect.
        /// - A static property is walked, like an instance one. A shared static object that is
        ///   invalid fails every model of that type. Type.GetProperties includes static members.
        /// - A Lazy&lt;T&gt; property is walked through Value, so validation runs the factory.
        /// - A Task&lt;T&gt; property is walked through Result. A completed task is fine.
        ///   See the class summary for a task that has not completed.
        /// - A property declared as an interface is validated by the type of the object in it.
        /// See: https://learn.microsoft.com/dotnet/api/system.type.getproperties
        /// See: https://learn.microsoft.com/dotnet/api/system.lazy-1.value
        /// See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.task-1.result
        /// </summary>
        public class MembersWithSideEffects
        {
            public class WithStatic
            {
                public static Leaf Shared { get; } = new Leaf();

                public string Own { get; set; } = "fine";
            }

            public interface IShape
            {
            }

            public class Circle : IShape
            {
                [Range(1, 10)]
                public int Radius { get; set; }
            }

            [Fact]
            public void Invalid_static_property_fails_every_instance()
            {
                var (valid, errors) = Run(new WithStatic());

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Shared.Name" + NameRequired), errors);
            }

            [Fact]
            public void Lazy_property_runs_its_factory_once()
            {
                var runs = 0;
                var lazy = new Lazy<Leaf>(() =>
                {
                    runs++;
                    return new Leaf();
                });

                var (valid, errors) = Run(new Holder<Lazy<Leaf>> { Value = lazy });

                Assert.False(valid);
                Assert.Equal(1, runs);
                Assert.Equal(ResultText.Expect("Value.Value.Name" + NameRequired), errors);
            }

            [Fact]
            public void Completed_task_property_is_validated_through_its_result()
            {
                var (valid, errors) = Run(new Holder<Task<Leaf>> { Value = Task.FromResult(new Leaf()) });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value.Result.Name" + NameRequired), errors);
            }

            [Fact]
            public void Interface_property_is_validated_by_the_runtime_type()
            {
                var (valid, errors) = Run(new Holder<IShape> { Value = new Circle { Radius = 99 } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value.Radius | The field Radius must be between 1 and 10."), errors);
            }
        }

        /// <summary>
        /// Collections that are not a plain List or array.
        /// - A non-generic Hashtable yields DictionaryEntry structs with a Value property of type object.
        /// - A dictionary key that is an object has its own attributes checked, through the Key property.
        /// - An ExpandoObject is an IDictionary of string and object.
        /// - A dictionary of object that holds another dictionary reaches the leaf through each Value.
        /// - A class that implements IEnumerable of two element types is enumerated through
        ///   the non-generic GetEnumerator, so the items are what that method yields.
        /// - A list that contains itself ends, because each object is visited once.
        /// See: https://learn.microsoft.com/dotnet/api/system.collections.dictionaryentry
        /// See: https://learn.microsoft.com/dotnet/api/system.dynamic.expandoobject
        /// </summary>
        public class ExoticCollections
        {
            public class KeyedByObject
            {
                public Dictionary<Leaf, string> Map { get; set; }
            }

            public class TwoElementTypes : IEnumerable<Leaf>, IEnumerable<int>
            {
                IEnumerator<Leaf> IEnumerable<Leaf>.GetEnumerator()
                {
                    yield return new Leaf();
                }

                IEnumerator<int> IEnumerable<int>.GetEnumerator()
                {
                    yield return 1;
                }

                IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Leaf>)this).GetEnumerator();
            }

            [Fact]
            public void Hashtable_values_are_validated()
            {
                var (valid, errors) = Run(new Holder<Hashtable> { Value = new Hashtable { ["k"] = new Leaf() } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Value.Name" + NameRequired), errors);
            }

            [Fact]
            public void Dictionary_keys_that_are_objects_are_validated()
            {
                var (valid, errors) = Run(new KeyedByObject { Map = new Dictionary<Leaf, string> { [new Leaf()] = "x" } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Map[0].Key.Name" + NameRequired), errors);
            }

            [Fact]
            public void ExpandoObject_values_are_validated()
            {
                dynamic expando = new ExpandoObject();
                expando.item = new Leaf();

                var (valid, errors) = Run(new Holder<ExpandoObject> { Value = expando });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Value.Name" + NameRequired), errors);
            }

            [Fact]
            public void Dictionary_of_object_holding_a_dictionary_is_validated()
            {
                var (valid, errors) = Run(new Holder<Dictionary<string, object>>
                {
                    Value = new Dictionary<string, object>
                    {
                        ["a"] = new Dictionary<string, object> { ["b"] = new Leaf() },
                    },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Value[0].Value.Name" + NameRequired), errors);
            }

            [Fact]
            public void Class_with_two_element_types_is_enumerated_through_the_non_generic_enumerator()
            {
                var (valid, errors) = Run(new Holder<TwoElementTypes> { Value = new TwoElementTypes() });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
            }

            [Fact]
            public void List_that_contains_itself_ends()
            {
                var list = new List<object>();
                list.Add(list);

                var (valid, errors) = Run(new Holder<List<object>> { Value = list });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Xml_document_object_model_is_walked_without_error()
            {
                // An XDocument is a graph of nodes that point to their parent, siblings and children.
                var (valid, errors) = Run(new Holder<XDocument> { Value = XDocument.Parse("<a><b/><c/></a>") });

                Assert.True(valid);
                Assert.Empty(errors);
            }
        }

        /// <summary>
        /// Properties whose type made the walk throw or never end. Reading some properties of a
        /// framework type throws, and the validator reads every property of a reference type.
        /// The exception reached the caller as a TargetInvocationException.
        /// - A relative Uri throws from Segments and other members that need an absolute Uri.
        /// - A delegate has a Method property, which returns a MethodInfo whose own properties throw.
        /// - DirectoryInfo and FileInfo overflowed the stack, which cannot be caught. Each read of
        ///   DirectoryInfo.Root returns a new DirectoryInfo that has its own Root, and neither
        ///   overrides Equals, so the walk never met an object it had seen.
        /// These types are now on the validator's deny list (IsUnsafeToWalk), so they are neither
        /// validated nor walked. ValidatorHardeningTests.FrameworkTypes covers the rest of the list.
        /// If the deny list loses DirectoryInfo or FileInfo, those tests crash the test host
        /// instead of failing.
        /// See: https://learn.microsoft.com/dotnet/api/system.uri.segments
        /// See: https://learn.microsoft.com/dotnet/api/system.delegate.method
        /// See: https://learn.microsoft.com/dotnet/api/system.io.directoryinfo.root
        /// </summary>
        public class MembersThatThrow
        {
            [Fact]
            public void Relative_uri_property_does_not_throw()
            {
                var valid = false;
                var ex = Record.Exception(() =>
                    valid = Run(new Holder<Uri> { Value = new Uri("/api/items", UriKind.Relative) }).Valid);

                Assert.Null(ex);
                Assert.True(valid);
            }

            [Fact]
            public void Delegate_property_does_not_throw()
            {
                var valid = false;
                var ex = Record.Exception(() =>
                    valid = Run(new Holder<Action> { Value = () => { } }).Valid);

                Assert.Null(ex);
                Assert.True(valid);
            }

            [Fact]
            public void Directory_info_property_does_not_overflow()
            {
                var (valid, errors) = Run(new Holder<DirectoryInfo> { Value = new DirectoryInfo(Path.GetTempPath()) });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void File_info_property_does_not_overflow()
            {
                var (valid, errors) = Run(new Holder<FileInfo> { Value = new FileInfo(Path.Combine(Path.GetTempPath(), "missing.txt")) });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Absolute_uri_property_does_not_throw()
            {
                var (valid, errors) = Run(new Holder<Uri> { Value = new Uri("https://example.com/a") });

                Assert.True(valid);
                Assert.Empty(errors);
            }
        }
    }
}
