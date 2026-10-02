using System;
using System.Collections;
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using System.Collections.Immutable;
#endif
using System.Collections.ObjectModel;
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
    /// - A spec. It is not skipped and fails until the fix lands. It states the behavior the fix gives.
    /// Every result below is the same on release 2.2.0 and on the current code, except the
    /// framework types in MembersThatThrow, which are no longer walked, and the collections in
    /// CollectionsInsideCollections, which 3.0 enumerates.
    /// Not covered here, because it stops the test run: a Task that has not completed makes the
    /// walk read Task.Result, which waits forever. The open decision, with skipped specs, is in
    /// TaskPropertyTests.
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
        /// A collection inside a collection. Releases 2.0.0 to 2.3.3 walk the properties of an item
        /// and enumerate a property that is a collection. They never enumerate an item that is itself
        /// a collection, because an item is not a property. So a List of List, a List of HashSet, or
        /// an object array that holds a List passed validation with invalid objects inside.
        /// Some shapes worked by accident. An array or an ArrayList has a public SyncRoot property
        /// that returns the collection itself. The validator walked that property and enumerated it,
        /// so a jagged array reported the path "Value[0].SyncRoot[0].Name". A List of Dictionary
        /// worked through the public Values property, with the path "Value[0].Values[0].Name".
        /// Release 3.0 enumerates an item that is a collection, and reports the index of each level:
        /// "Value[0][0].Name". The tests in this class state that behavior. Most fail on 2.3.3 and
        /// earlier: the shapes that passed silently report nothing, and the shapes that worked by
        /// accident report a different path. The guards for null items, empty collections and
        /// collections of values pass on both.
        /// An item that is a collection is still validated as an object first, as before: its own
        /// attributes run and its own properties are walked. Then it is enumerated. Enumerating
        /// must not remove an error that 2.3.3 reports (see CollectionsWithMembersOfTheirOwn).
        /// The items are enumerated before the properties are walked. An object is validated once,
        /// so a Leaf that is reachable both ways is reported at the enumeration path.
        /// A dictionary item is enumerated as KeyValuePair items, so a List of Dictionary reports
        /// "Value[0][0].Value.Name", the same member names as a dictionary held in a property.
        /// A collection of a struct type, such as ImmutableArray or a readonly struct that implements
        /// IEnumerable, is enumerated like any other. The validator does not walk into a struct
        /// that is held in a property, but an item that hides objects must not pass.
        /// See: https://learn.microsoft.com/dotnet/api/system.array.syncroot
        /// </summary>
        public class CollectionsInsideCollections
        {
            [Fact]
            public void List_of_lists_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<List<Leaf>>>
                {
                    Value = new List<List<Leaf>> { new List<Leaf> { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Object_array_holding_a_list_is_validated()
            {
                var (valid, errors) = Run(new Holder<object[]>
                {
                    Value = new object[] { new List<Leaf> { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void List_of_hash_sets_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<HashSet<Leaf>>>
                {
                    Value = new List<HashSet<Leaf>> { new HashSet<Leaf> { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void List_of_read_only_collections_is_validated()
            {
                // A read-only collection is typical for a type that exposes IReadOnlyList of IReadOnlyList.
                var (valid, errors) = Run(new Holder<IReadOnlyList<IReadOnlyList<Leaf>>>
                {
                    Value = new List<IReadOnlyList<Leaf>> { new ReadOnlyCollection<Leaf>(new[] { new Leaf() }) },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Three_levels_of_lists_are_validated()
            {
                var (valid, errors) = Run(new Holder<List<List<List<Leaf>>>>
                {
                    Value = new List<List<List<Leaf>>>
                    {
                        new List<List<Leaf>> { new List<Leaf> { new Leaf() } },
                    },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Path_has_the_index_of_each_level()
            {
                var (valid, errors) = Run(new Holder<List<List<Leaf>>>
                {
                    Value = new List<List<Leaf>>
                    {
                        new List<Leaf> { new Leaf { Name = "a" }, new Leaf { Name = "b" } },
                        new List<Leaf> { new Leaf { Name = "c" }, new Leaf() },
                    },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[1][1].Name" + NameRequired), errors);
            }

            [Fact]
            public void Null_items_count_in_the_index_at_each_level()
            {
                var (valid, errors) = Run(new Holder<List<List<Leaf>>>
                {
                    Value = new List<List<Leaf>>
                    {
                        null,
                        new List<Leaf> { null, new Leaf() },
                    },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[1][1].Name" + NameRequired), errors);
            }

            [Fact]
            public void Each_invalid_object_is_reported_with_its_own_path()
            {
                var (valid, errors) = Run(new Holder<List<List<Leaf>>>
                {
                    Value = new List<List<Leaf>>
                    {
                        new List<Leaf> { new Leaf() },
                        new List<Leaf> { new Leaf() },
                    },
                });

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect("Value[0][0].Name" + NameRequired, "Value[1][0].Name" + NameRequired),
                    errors);
            }

            [Fact]
            public void Inner_list_that_appears_twice_is_reported_once()
            {
                // The validator visits each object once, so an object that is reachable twice is
                // reported at the first path only. The same holds for an inner collection.
                var shared = new List<Leaf> { new Leaf() };

                var (valid, errors) = Run(new Holder<List<List<Leaf>>>
                {
                    Value = new List<List<Leaf>> { shared, shared },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            // The test above passes even if the inner list is walked twice, because the Leaf is
            // marked as visited on the first walk. Counting the enumerations shows that the shared
            // collection is walked once.
            [Fact]
            public void Inner_list_that_appears_twice_is_enumerated_once()
            {
                var shared = new CountingLeaves();

                var (valid, errors) = Run(new Holder<List<CountingLeaves>> { Value = new List<CountingLeaves> { shared, shared } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
                Assert.Equal(1, shared.Enumerations);
            }

            public class CountingLeaves : IEnumerable<Leaf>
            {
                public int Enumerations { get; private set; }

                public IEnumerator<Leaf> GetEnumerator()
                {
                    Enumerations++;
                    yield return new Leaf();
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            // A sealed class cannot have a derived type, and this one has no attributes and no
            // properties, so the type check IsLeafType says that it can never produce a result. That
            // check looks at the type only, not at the Leaf objects the class yields when enumerated.
            // A List of SealedBag is then skipped as a collection of leaf values, and a fix that
            // enumerates only the items that fail the check would still miss it.
            // See: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/sealed
            [Fact]
            public void List_of_sealed_collections_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<SealedBag>>
                {
                    Value = new List<SealedBag> { new SealedBag(new Leaf()) },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Object_array_holding_a_sealed_collection_is_validated()
            {
                var (valid, errors) = Run(new Holder<object[]>
                {
                    Value = new object[] { new SealedBag(new Leaf()) },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            public sealed class SealedBag : IEnumerable<Leaf>
            {
                private readonly Leaf[] _items;

                public SealedBag(params Leaf[] items)
                {
                    _items = items;
                }

                public IEnumerator<Leaf> GetEnumerator() => ((IEnumerable<Leaf>)_items).GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

#if NET8_0_OR_GREATER
            // ImmutableList is sealed, and System.Collections.Immutable is not part of net481.
            // See: https://learn.microsoft.com/dotnet/api/system.collections.immutable.immutablelist-1
            [Fact]
            public void List_of_immutable_lists_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<ImmutableList<Leaf>>>
                {
                    Value = new List<ImmutableList<Leaf>> { ImmutableList.Create(new Leaf()) },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }
#endif

            // StructsAreNotWalked.LeafBag is a readonly struct that implements IEnumerable of Leaf.
            // A boxed struct is not a sealed class, but IsLeafType treats both the same way, so this
            // is the struct form of the SealedBag gap above.
            [Fact]
            public void List_of_struct_collections_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<StructsAreNotWalked.LeafBag>>
                {
                    Value = new List<StructsAreNotWalked.LeafBag> { new StructsAreNotWalked.LeafBag(new Leaf()) },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Object_array_holding_a_struct_collection_is_validated()
            {
                var (valid, errors) = Run(new Holder<object[]>
                {
                    Value = new object[] { new StructsAreNotWalked.LeafBag(new Leaf()) },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

#if NET8_0_OR_GREATER
            // ImmutableArray is a struct. See StructCollectionPropertyTests for the same type held
            // in a property.
            // See: https://learn.microsoft.com/dotnet/api/system.collections.immutable.immutablearray-1
            [Fact]
            public void List_of_immutable_arrays_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<ImmutableArray<Leaf>>>
                {
                    Value = new List<ImmutableArray<Leaf>> { ImmutableArray.Create(new Leaf()) },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }
#endif

            [Fact]
            public void Lists_that_contain_each_other_end_and_report_the_invalid_object_once()
            {
                // The outer list is also an item of the inner list, so enumerating items that are
                // collections must not loop. The invalid object is on the inner list.
                var outer = new List<object>();
                var inner = new List<object> { outer, new Leaf() };
                outer.Add(inner);

                var (valid, errors) = Run(new Holder<List<object>> { Value = outer });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][1].Name" + NameRequired), errors);
            }

            [Fact]
            public void Null_and_empty_inner_collections_are_ignored()
            {
                var (valid, errors) = Run(new Holder<List<List<Leaf>>>
                {
                    Value = new List<List<Leaf>> { null, new List<Leaf>(), new List<Leaf> { null } },
                });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            // Items that are collections of values are not enumerated, like a property of that
            // type (see IsCollectionOfLeafType). This test counts the enumerations that start.
            // The result is the same either way, so only a count can tell the two apart.
            // An iterator method runs no code until the first MoveNext, so the count goes up there.
            [Fact]
            public void Inner_collection_of_values_is_not_enumerated()
            {
                var numbers = new CountingNumbers();

                var (valid, errors) = Run(new Holder<List<CountingNumbers>> { Value = new List<CountingNumbers> { numbers } });

                Assert.True(valid);
                Assert.Empty(errors);
                Assert.Equal(0, numbers.Enumerations);
            }

            public class CountingNumbers : IEnumerable<int>
            {
                public int Enumerations { get; private set; }

                public IEnumerator<int> GetEnumerator()
                {
                    Enumerations++;
                    yield return 1;
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [Fact]
            public void List_of_arrays_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<Leaf[]>>
                {
                    Value = new List<Leaf[]> { new[] { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Object_array_holding_an_array_is_validated()
            {
                var (valid, errors) = Run(new Holder<object[]>
                {
                    Value = new object[] { new[] { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void List_of_array_lists_is_validated()
            {
                // ArrayList is not generic, and like an array it has a public SyncRoot property.
                var (valid, errors) = Run(new Holder<List<ArrayList>>
                {
                    Value = new List<ArrayList> { new ArrayList { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            [Fact]
            public void List_of_dictionaries_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<Dictionary<string, Leaf>>>
                {
                    Value = new List<Dictionary<string, Leaf>>
                    {
                        new Dictionary<string, Leaf> { ["k"] = new Leaf() },
                    },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Value.Name" + NameRequired), errors);
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

            // Up to release 2.3.3 the path was "Value[0].SyncRoot[0].Name".
            [Fact]
            public void Jagged_array_is_validated()
            {
                var (valid, errors) = Run(new Holder<Leaf[][]>
                {
                    Value = new[] { new[] { new Leaf() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }
        }

        /// <summary>
        /// Member names that are not plain property names, on an item of a collection.
        /// - A class-level attribute that passes ValidationContext.MemberName gives a null member
        ///   name, because no member is being validated. This is the usual way to write one.
        ///   The same holds for an IValidatableObject that yields a null member name.
        ///   The validator prefixes the path to each name, and a null name gives "Value[0]." with
        ///   nothing after the dot. Code that builds the path must not throw on null.
        /// - A member name that starts with "[", such as "[Totals]", is a name the item chose. It
        ///   is not the index of a nested collection, so the path keeps the dot: "Value[0].[Totals]".
        /// The tests also run on 2.3.3, which gives the same paths. Release 3.0 first read the
        /// name to decide whether it was an index, and threw NullReferenceException on null.
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.membername
        /// </summary>
        public class UnusualMemberNames
        {
            [AttributeUsage(AttributeTargets.Class)]
            public class ClassLevelAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
                    new ValidationResult("The item is not valid.", new[] { validationContext.MemberName });
            }

            [ClassLevel]
            public class ClassLevelItem
            {
            }

            public class SelfValidatingItem : IValidatableObject
            {
                public string MemberName { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield return new ValidationResult("The item is not valid.", new[] { MemberName });
                }
            }

            [Fact]
            public void Null_member_name_from_a_class_level_attribute_on_an_item()
            {
                var (valid, errors) = Run(new Holder<List<ClassLevelItem>> { Value = new List<ClassLevelItem> { new ClassLevelItem() } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0]. | The item is not valid."), errors);
            }

            [Fact]
            public void Null_member_name_from_Validate_on_an_item()
            {
                var (valid, errors) = Run(new Holder<List<SelfValidatingItem>> { Value = new List<SelfValidatingItem> { new SelfValidatingItem() } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0]. | The item is not valid."), errors);
            }

            // In a nested collection the path has the index of each level, then the empty name.
            // Nested collections are enumerated from 3.0, so 2.3.3 reports nothing here.
            [Fact]
            public void Null_member_name_on_an_item_of_a_nested_collection()
            {
                var (valid, errors) = Run(new Holder<List<List<ClassLevelItem>>>
                {
                    Value = new List<List<ClassLevelItem>> { new List<ClassLevelItem> { new ClassLevelItem() } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0]. | The item is not valid."), errors);
            }

            // A name that starts with a bracket on an item of a nested collection is still a name.
            // The path is the index of each level, a dot, then the name.
            [Fact]
            public void Member_name_that_starts_with_a_bracket_on_an_item_of_a_nested_collection()
            {
                var (valid, errors) = Run(new Holder<List<List<SelfValidatingItem>>>
                {
                    Value = new List<List<SelfValidatingItem>>
                    {
                        new List<SelfValidatingItem> { new SelfValidatingItem { MemberName = "[Totals]" } },
                    },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].[Totals] | The item is not valid."), errors);
            }

            [Fact]
            public void Member_name_that_starts_with_a_bracket_keeps_the_dot()
            {
                var (valid, errors) = Run(new Holder<List<SelfValidatingItem>>
                {
                    Value = new List<SelfValidatingItem> { new SelfValidatingItem { MemberName = "[Totals]" } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].[Totals] | The item is not valid."), errors);
            }
        }

        /// <summary>
        /// A collection as the object passed to the validator. The validator validates that object
        /// and walks its properties, but never enumerates it, because an item is only reached
        /// through a property. So a List that is the root hides its items, and a root array reports
        /// its items through SyncRoot. This is not changed by the fix for nested collections.
        /// These are limitation guards. If the validator learns to enumerate a root collection,
        /// the tests fail on purpose, so the change is deliberate. The path format for it is open.
        /// </summary>
        public class CollectionAsRootObject
        {
            [Fact]
            public void Items_of_a_root_list_are_not_validated()
            {
                var (valid, errors) = Run(new List<Leaf> { new Leaf() });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Items_of_a_root_array_are_validated_through_SyncRoot()
            {
                var (valid, errors) = Run(new[] { new Leaf() });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("SyncRoot[0].Name" + NameRequired), errors);
            }
        }

        /// <summary>
        /// A collection class that has members of its own, such as the paging data on a result list.
        /// The validator enumerates the collection, but never validates the collection object.
        /// Its own attributes do not run, and its own properties are not walked.
        /// The items are still validated.
        /// Whether the attributes of the collection object should run is open. Running them would
        /// fail models that pass today.
        /// This holds for a collection held in a property. A collection that is an item of another
        /// collection is validated as an object, so its attributes and IValidatableObject run today.
        /// Enumerating such an item (see CollectionsInsideCollections) must keep those errors,
        /// because dropping them would let a model that fails on 2.3.3 pass.
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

            // Guard. An item that is a collection is validated as an object, so its attribute runs.
            // This passes on 2.3.3. A fix that only enumerates the item would drop this error.
            [Fact]
            public void Attributes_on_a_collection_item_are_validated()
            {
                var (valid, errors) = Run(new Holder<List<PagedList<Leaf>>>
                {
                    Value = new List<PagedList<Leaf>> { new PagedList<Leaf> { Cursor = null } },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Cursor | The Cursor field is required."), errors);
            }

            // Spec. The item has its own error and an invalid object inside. Both are reported.
            [Fact]
            public void Attributes_and_items_of_a_collection_item_are_both_validated()
            {
                var page = new PagedList<Leaf> { Cursor = null };
                page.Add(new Leaf());

                var (valid, errors) = Run(new Holder<List<PagedList<Leaf>>> { Value = new List<PagedList<Leaf>> { page } });

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect("Value[0].Cursor | The Cursor field is required.", "Value[0][0].Name" + NameRequired),
                    errors);
            }

            public class ValidatableBag : List<Leaf>, IValidatableObject
            {
                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield return new ValidationResult("The bag is not valid.", new[] { "Total" });
                }
            }

            // Guard. IValidatableObject on an item that is a collection runs on 2.3.3, and must keep running.
            [Fact]
            public void Validate_on_a_collection_item_runs()
            {
                var (valid, errors) = Run(new Holder<List<ValidatableBag>> { Value = new List<ValidatableBag> { new ValidatableBag() } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Total | The bag is not valid."), errors);
            }
        }

        /// <summary>
        /// Structs. The validator walks into properties of reference types only, so a struct
        /// property is checked for its own validation attributes by Validator when the parent is
        /// validated, but nothing inside the struct is walked. A property of the struct that
        /// carries an attribute is never checked. The exception is a struct that is a collection,
        /// such as ImmutableArray&lt;T&gt;: its items are validated, as an item of another collection
        /// and as a property (see StructCollectionPropertyTests).
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
        /// These types are now on the validator's deny list (IsUnsafeToWalk). The object is still
        /// validated, but the walk skips the properties these framework types declare.
        /// ValidatorHardeningTests.FrameworkTypes covers the rest of the list, and
        /// UriValidationTests shows how to validate a Uri.
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
