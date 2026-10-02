using System;
using System.Collections;
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using System.Collections.Immutable;
#endif
using System.ComponentModel.DataAnnotations;
using RecursiveDataAnnotationsValidation.Attributes;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// A collection that is a struct, held in a property, such as Holder&lt;ImmutableArray&lt;T&gt;&gt;.
    ///
    /// Why it matters. The validator walks only the properties of a reference type (see IsWalked),
    /// so a property whose type is a struct used to be skipped. That included a struct that is a
    /// collection, such as ImmutableArray&lt;T&gt;. The validator enumerates an item that is a
    /// collection, and it does so for a struct too, because an item is validated as an object
    /// whatever its type is. So the same ImmutableArray of invalid objects was:
    /// - validated when it is an item, such as List&lt;ImmutableArray&lt;T&gt;&gt;, and
    /// - not validated, silently, when it is the value of a property.
    /// A model could pass validation because the collection sat one level higher.
    ///
    /// The rule now. IsWalked admits a property of a struct type when the struct is a collection
    /// that is not a collection of leaf types, and a Nullable of such a struct. The walk reads the
    /// value, which is boxed, and enumerates it like any other collection. The path is the same
    /// as for a collection that is a class: Value[0].Name.
    /// - A struct that is not a collection, such as a Money, is walked when it has something to
    ///   validate, and skipped when it is a leaf type (see OddShapeTests.StructProperties).
    /// - A struct in a property that is its default value, such as a default ImmutableArray or
    ///   ArraySegment, is skipped, as an item is (see NestedCollectionEdgeCaseTests, Case 2). It
    ///   passed before, and enumerating it would add a crash. The check compares memory, and does
    ///   not call the struct's Equals (see StructsWithOddEquals). A struct with no fields is
    ///   always default, so a property of that struct type is skipped too. Only a property declared
    ///   as a struct is skipped this way. A property declared as an interface or object was always
    ///   enumerated, and still is.
    /// - A struct collection that throws when enumerated throws from the property too, like an
    ///   item and like a class. This is a change for the few models that hold one.
    /// - Check 4 of IsLeafType uses IsWalked, so a struct with such a property is no longer a
    ///   leaf type. That is the right answer, because the validator now looks inside it.
    /// - [SkipRecursiveValidation] on the property still skips it.
    /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/struct
    ///
    /// A model that passed before can fail now. The path of an error is new, so no member name
    /// that callers matched has changed.
    /// </summary>
    public class StructCollectionPropertyTests
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

        private const string NameRequired = " | The Name field is required.";

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

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

        /// <summary>
        /// A struct cannot count its own enumerations, because it is copied. This one counts in an
        /// object that every copy shares.
        /// </summary>
        public readonly struct CountedNumbers : IEnumerable<int>
        {
            public CountedNumbers(Counter counter)
            {
                Counter = counter;
            }

            public Counter Counter { get; }

            public IEnumerator<int> GetEnumerator()
            {
                Counter.Enumerations++;
                yield return 1;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public class Counter
        {
            public int Enumerations { get; set; }
        }

        // The field makes a value that is not default(ThrowingStructBag). A struct with no fields
        // always equals its default, and the validator skips a default struct (see IsDefaultStruct).
        public readonly struct ThrowingStructBag : IEnumerable<Leaf>
        {
            private readonly int _marker;

            public ThrowingStructBag(int marker)
            {
                _marker = marker;
            }

            public IEnumerator<Leaf> GetEnumerator() => throw new InvalidOperationException("enumeration failed");

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // The same struct is validated as an item and as a property, with the same member names
        // except for the extra index of the list.
        [Fact]
        public void Struct_collection_is_validated_as_an_item_and_as_a_property()
        {
            var asItem = Run(new Holder<List<LeafBag>> { Value = new List<LeafBag> { new LeafBag(new Leaf()) } });
            var asProperty = Run(new Holder<LeafBag> { Value = new LeafBag(new Leaf()) });

            Assert.False(asItem.Valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), asItem.Errors);
            Assert.False(asProperty.Valid);
            Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), asProperty.Errors);
        }

        [Fact]
        public void Struct_collection_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<LeafBag> { Value = new LeafBag(new Leaf()) });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
        }

        // A nullable struct is boxed as the struct itself when it has a value.
        // See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/nullable-value-types#boxing-and-unboxing
        [Fact]
        public void Nullable_struct_collection_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<LeafBag?> { Value = new LeafBag(new Leaf()) });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
        }

        [Fact]
        public void Nullable_struct_collection_property_without_a_value_is_valid()
        {
            var (valid, errors) = Run(new Holder<LeafBag?> { Value = null });

            Assert.True(valid);
            Assert.Empty(errors);
        }

        [Fact]
        public void Struct_collection_property_below_a_class_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<Holder<LeafBag>>
            {
                Value = new Holder<LeafBag> { Value = new LeafBag(new Leaf()) },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value.Value[0].Name" + NameRequired), errors);
        }

        [Fact]
        public void Struct_collection_property_in_an_item_is_validated()
        {
            var (valid, errors) = Run(new Holder<List<Holder<LeafBag>>>
            {
                Value = new List<Holder<LeafBag>> { new Holder<LeafBag> { Value = new LeafBag(new Leaf()) } },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0].Value[0].Name" + NameRequired), errors);
        }

        // The error of each object is reported once, and the valid ones are not reported.
        [Fact]
        public void Only_the_invalid_items_of_a_struct_collection_property_are_reported()
        {
            var (valid, errors) = Run(new Holder<LeafBag>
            {
                Value = new LeafBag(new Leaf { Name = "a" }, null, new Leaf(), new Leaf { Name = "b" }),
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[2].Name" + NameRequired), errors);
        }

        public class TwoBags
        {
            public LeafBag First { get; set; }

            public LeafBag Second { get; set; }
        }

        public class SkippedBag
        {
            [SkipRecursiveValidation]
            public LeafBag Value { get; set; }
        }

        // The items of a struct collection are objects of a class, so the reference check applies to
        // them as to any other object: one that two properties share is validated, and reported, once.
        // A struct item has no identity of its own (see StructsReachedByTwoRoutesTests).
        [Fact]
        public void Object_shared_by_two_struct_collection_properties_is_reported_once()
        {
            var shared = new Leaf();

            var (valid, errors) = Run(new TwoBags { First = new LeafBag(shared), Second = new LeafBag(shared) });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("First[0].Name" + NameRequired), errors);
        }

        // The opt-out works for a struct collection as for any other property.
        [Fact]
        public void Struct_collection_property_marked_to_skip_is_not_enumerated()
        {
            var (valid, errors) = Run(new SkippedBag { Value = new LeafBag(new Leaf()) });

            Assert.True(valid);
            Assert.Empty(errors);
        }

        // Guard. A struct collection of leaf types has nothing to validate, like a List of int, so
        // it is not enumerated. The count shows it, because the result is the same either way.
        [Fact]
        public void Struct_collection_of_values_is_not_enumerated()
        {
            var counter = new Counter();

            var (valid, errors) = Run(new Holder<CountedNumbers> { Value = new CountedNumbers(counter) });

            Assert.True(valid);
            Assert.Empty(errors);
            Assert.Equal(0, counter.Enumerations);
        }

        // A struct collection that throws when it is enumerated throws from a property, like an
        // item (NestedCollectionEdgeCaseTests, Case 1). It passed before 3.0, because the property was not read.
        [Fact]
        public void Struct_collection_property_that_throws_when_enumerated_propagates()
        {
            var ex = Record.Exception(() => Run(new Holder<ThrowingStructBag> { Value = new ThrowingStructBag(1) }));

            Assert.IsType<InvalidOperationException>(ex);
        }

        // A default struct is skipped, as an item is, so one that would throw passes.
        [Fact]
        public void Default_struct_collection_that_throws_when_enumerated_is_skipped()
        {
            var (valid, errors) = Run(new Holder<ThrowingStructBag> { Value = default });

            Assert.True(valid);
            Assert.Empty(errors);
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void Immutable_array_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<ImmutableArray<Leaf>> { Value = ImmutableArray.Create(new Leaf()) });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
        }

        [Fact]
        public void Immutable_array_of_immutable_arrays_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<ImmutableArray<ImmutableArray<Leaf>>>
            {
                Value = ImmutableArray.Create(ImmutableArray.Create(new Leaf())),
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
        }

        // Guard. A default ImmutableArray in a property passed before the fix and still passes.
        // Enumerating it throws, so the fix has to skip it, as it does for an item.
        [Fact]
        public void Default_immutable_array_property_is_valid()
        {
            var (valid, errors) = Run(new Holder<ImmutableArray<Leaf>> { Value = default });

            Assert.True(valid);
            Assert.Empty(errors);
        }

        [Fact]
        public void Immutable_array_of_values_property_is_valid()
        {
            var (valid, errors) = Run(new Holder<ImmutableArray<int>> { Value = ImmutableArray.Create(1, 2) });

            Assert.True(valid);
            Assert.Empty(errors);
        }
#endif

        /// <summary>
        /// Structs whose own Equals is not a reliable test for "nothing was set". The validator skips a
        /// default struct, because enumerating one can throw, and it must decide that by the memory
        /// of the struct, not by calling the Equals of the caller's type. A model that Equals its
        /// default by accident would otherwise pass while it holds invalid objects, and an Equals
        /// that throws would end validation. The types here are unusual, but a model that passed
        /// only because of such an Equals is a false pass.
        /// See: https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.runtimehelpers.equals
        /// </summary>
        public class StructsWithOddEquals
        {
            /// <summary>Equals compares the Id only, so a bag with Id 0 equals default(IdBag) whatever it holds.</summary>
            public readonly struct IdBag : IEnumerable<Leaf>
            {
                private readonly Leaf[] _items;

                public IdBag(int id, params Leaf[] items)
                {
                    Id = id;
                    _items = items;
                }

                public int Id { get; }

                public override bool Equals(object obj) => obj is IdBag other && other.Id == Id;

                public override int GetHashCode() => Id;

                public IEnumerator<Leaf> GetEnumerator() => ((IEnumerable<Leaf>)_items).GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            /// <summary>
            /// Equals compares the items, and throws when it is compared with default(SequenceBag),
            /// because the default value has no array.
            /// </summary>
            public readonly struct SequenceBag : IEnumerable<Leaf>
            {
                private readonly Leaf[] _items;

                public SequenceBag(params Leaf[] items)
                {
                    _items = items;
                }

                public override bool Equals(object obj) => obj is SequenceBag other && System.Linq.Enumerable.SequenceEqual(_items, other._items);

                public override int GetHashCode() => 0;

                public IEnumerator<Leaf> GetEnumerator() => ((IEnumerable<Leaf>)_items).GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            /// <summary>A struct with no fields. It always equals default, and it enumerates a shared list.</summary>
            public readonly struct RegistryView : IEnumerable<Leaf>
            {
                public static readonly List<Leaf> Registry = new List<Leaf> { new Leaf() };

                public IEnumerator<Leaf> GetEnumerator() => Registry.GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            // Guard. On 2.3.3 an interface-typed property was walked and enumerated whatever the
            // value was. A bag that Equals default by accident must not be skipped there.
            [Fact]
            public void Interface_typed_property_holding_a_bag_that_equals_default_is_validated()
            {
                var (valid, errors) = Run(new Holder<IEnumerable<Leaf>> { Value = new IdBag(0, new Leaf()) });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Object_typed_property_holding_a_bag_that_equals_default_is_validated()
            {
                var (valid, errors) = Run(new Holder<object> { Value = new IdBag(0, new Leaf()) });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
            }

            // A field-less struct is a default struct by memory too, and a property of that struct type is
            // skipped, as a default ImmutableArray is. Declared as an interface it is still enumerated.
            [Fact]
            public void Interface_typed_property_holding_a_field_less_struct_collection_is_validated()
            {
                var (valid, errors) = Run(new Holder<IEnumerable<Leaf>> { Value = new RegistryView() });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Object_typed_property_holding_a_field_less_struct_collection_is_validated()
            {
                var (valid, errors) = Run(new Holder<object> { Value = new RegistryView() });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Struct_typed_property_with_a_bag_that_equals_default_is_validated()
            {
                var (valid, errors) = Run(new Holder<IdBag> { Value = new IdBag(0, new Leaf()) });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
            }

            [Fact]
            public void Struct_item_that_equals_default_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { new IdBag(0, new Leaf()) } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
            }

            // The Equals of the struct is not called, so one that throws cannot end validation.
            // Before, a model with such a bag passed on 2.3.3 and threw from 3.0 on.
            [Fact]
            public void Struct_typed_property_with_an_Equals_that_throws_is_validated()
            {
                var (valid, errors) = Run(new Holder<SequenceBag> { Value = new SequenceBag(new Leaf { Name = "a" }) });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Interface_typed_property_with_an_Equals_that_throws_is_validated()
            {
                var (valid, errors) = Run(new Holder<IEnumerable<Leaf>> { Value = new SequenceBag(new Leaf { Name = "a" }) });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Struct_item_with_an_Equals_that_throws_is_validated()
            {
                var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { new SequenceBag(new Leaf { Name = "a" }) } });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Default_struct_with_an_Equals_that_throws_is_skipped()
            {
                var (valid, errors) = Run(new Holder<SequenceBag> { Value = default });

                Assert.True(valid);
                Assert.Empty(errors);
            }
        }

#if NET8_0_OR_GREATER
        // A dictionary value that is a struct collection. The KeyValuePair item of the dictionary is
        // walked, and so is the struct collection that its Value property holds.
        [Fact]
        public void Dictionary_value_that_is_a_struct_collection_is_validated()
        {
            var (valid, errors) = Run(new Holder<Dictionary<string, ImmutableArray<Leaf>>>
            {
                Value = new Dictionary<string, ImmutableArray<Leaf>> { ["a"] = ImmutableArray.Create(new Leaf()) },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0].Value[0].Name" + NameRequired), errors);
        }

        [Fact]
        public void Dictionary_value_that_is_a_default_struct_collection_is_valid()
        {
            var (valid, errors) = Run(new Holder<Dictionary<string, ImmutableArray<Leaf>>>
            {
                Value = new Dictionary<string, ImmutableArray<Leaf>> { ["a"] = default },
            });

            Assert.True(valid);
            Assert.Empty(errors);
        }
#endif
    }
}
