using System;
using System.Collections;
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using System.Collections.Immutable;
#endif
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// A collection that is a struct, held in a property. Not fixed in 2.4.0. The specs here are
    /// skipped and describe the behavior planned for the next release.
    ///
    /// The inconsistency. The validator walks only the properties of a reference type (see
    /// IsWalked), so a property whose type is a struct is never read. That includes a struct
    /// that is a collection, such as ImmutableArray&lt;T&gt;. Since 2.4.0 the validator enumerates an
    /// item that is a collection, and it does so for a struct too, because an item is validated as
    /// an object whatever its type is. So the same ImmutableArray of invalid objects is:
    /// - validated when it is an item, such as List&lt;ImmutableArray&lt;T&gt;&gt;, and
    /// - not validated, silently, when it is the value of a property.
    /// A model can pass validation because the collection sits one level higher.
    /// Both behaviors are pinned below, in a guard that passes today and fails on purpose when the
    /// property case is fixed.
    ///
    /// Why a struct property is skipped. The rule suits a Point or a Money, where the validator
    /// has nothing to walk into. It covers a struct that is a collection as a side effect, and
    /// nothing in the code or the tests records a decision about collections. A struct property with
    /// an attribute on the struct's own members is still not walked (see
    /// OddShapeTests.StructsAreNotWalked), and this fix does not change that. A property that holds
    /// a struct is only enumerated when the struct is a collection, so that a model does not pass
    /// because the collection sits one level higher.
    /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/struct
    ///
    /// The plan for the fix:
    /// - IsWalked admits a property of a struct type when the struct is a collection that is not a
    ///   collection of leaf types, and a Nullable of such a struct. A struct that is not a
    ///   collection, such as a Money, is still skipped.
    /// - The walk reads the value, which is boxed, and enumerates it like any other collection.
    ///   The path is the same as for a collection that is a class: Value[0].Name.
    /// - A struct in a property that equals its default value, such as a default ImmutableArray or
    ///   ArraySegment, is skipped, as an item is (see NestedCollectionEdgeCaseTests, Case 2). It
    ///   passes today, so enumerating it would add a crash.
    /// - A struct collection that throws when enumerated throws from the property too, like an
    ///   item and like a class. This is a change for the few models that hold one.
    /// - Check 4 of IsLeafType uses IsWalked, so a struct with such a property is no longer a
    ///   leaf type. That is the right answer, because the validator now looks inside it.
    ///
    /// When the fix lands:
    /// - Unskip the specs below.
    /// - Delete Struct_collection_is_validated_as_an_item_but_not_as_a_property, which pins the gap.
    /// - Replace OddShapeTests.StructsAreNotWalked.Struct_collection_items_are_not_validated, which
    ///   pins the same gap, and update the comment of that class, which mentions ImmutableArray.
    /// - Add a Fixed entry to the changelog. A model that passes today can fail. List the changed
    ///   member name format as BREAKING only if one exists. A property that was never walked has no
    ///   path to change.
    /// </summary>
    public class StructCollectionPropertyTests
    {
        private const string Reason = "Next release. A struct collection held in a property is not enumerated yet.";

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

        public readonly struct ThrowingStructBag : IEnumerable<Leaf>
        {
            public IEnumerator<Leaf> GetEnumerator() => throw new InvalidOperationException("enumeration failed");

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// Guard that pins the gap. The same struct is validated as an item, and passes as a
        /// property. It fails on purpose when the property case is fixed.
        /// </summary>
        [Fact]
        public void Struct_collection_is_validated_as_an_item_but_not_as_a_property()
        {
            var asItem = Run(new Holder<List<LeafBag>> { Value = new List<LeafBag> { new LeafBag(new Leaf()) } });
            var asProperty = Run(new Holder<LeafBag> { Value = new LeafBag(new Leaf()) });

            Assert.False(asItem.Valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), asItem.Errors);
            Assert.True(asProperty.Valid);
            Assert.Empty(asProperty.Errors);
        }

        [Fact(Skip = Reason)]
        public void Struct_collection_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<LeafBag> { Value = new LeafBag(new Leaf()) });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
        }

        // A nullable struct is boxed as the struct itself when it has a value.
        // See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/nullable-value-types#boxing-and-unboxing
        [Fact(Skip = Reason)]
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

        [Fact(Skip = Reason)]
        public void Struct_collection_property_below_a_class_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<Holder<LeafBag>>
            {
                Value = new Holder<LeafBag> { Value = new LeafBag(new Leaf()) },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value.Value[0].Name" + NameRequired), errors);
        }

        [Fact(Skip = Reason)]
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
        [Fact(Skip = Reason)]
        public void Only_the_invalid_items_of_a_struct_collection_property_are_reported()
        {
            var (valid, errors) = Run(new Holder<LeafBag>
            {
                Value = new LeafBag(new Leaf { Name = "a" }, null, new Leaf(), new Leaf { Name = "b" }),
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[2].Name" + NameRequired), errors);
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
        // item (NestedCollectionEdgeCaseTests, Case 1). It passes today, because it is not read.
        [Fact(Skip = Reason)]
        public void Struct_collection_property_that_throws_when_enumerated_propagates()
        {
            var ex = Record.Exception(() => Run(new Holder<ThrowingStructBag> { Value = new ThrowingStructBag() }));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void Struct_collection_that_throws_when_enumerated_passes_today()
        {
            var (valid, errors) = Run(new Holder<ThrowingStructBag> { Value = new ThrowingStructBag() });

            Assert.True(valid);
            Assert.Empty(errors);
        }

#if NET8_0_OR_GREATER
        [Fact(Skip = Reason)]
        public void Immutable_array_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<ImmutableArray<Leaf>> { Value = ImmutableArray.Create(new Leaf()) });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0].Name" + NameRequired), errors);
        }

        [Fact(Skip = Reason)]
        public void Immutable_array_of_immutable_arrays_property_is_validated()
        {
            var (valid, errors) = Run(new Holder<ImmutableArray<ImmutableArray<Leaf>>>
            {
                Value = ImmutableArray.Create(ImmutableArray.Create(new Leaf())),
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Name" + NameRequired), errors);
        }

        // Guard. A default ImmutableArray in a property passes today and must pass after the fix.
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
    }
}
