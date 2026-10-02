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
    /// Open cases for the fix that enumerates an item that is itself a collection (see
    /// OddShapeTests.CollectionsInsideCollections). In 2.3.3 and earlier an item that is a
    /// collection is validated as an object but never enumerated. After the fix it is also
    /// enumerated, and enumerating can do two things that validating the object never did:
    /// it can throw, and it can run for a long time. None of these cases is decided yet.
    /// Each case has the reasoning and a recommendation. The specs are skipped until a ruling.
    ///
    /// What the validator does today when it enumerates a collection that a property holds:
    /// - It does not catch anything. The exception leaves TryValidateObjectRecursive (see
    ///   ThrowingMemberTests). A foreach calls GetEnumerator and MoveNext directly, not through
    ///   reflection, so the exception is not wrapped in a TargetInvocationException.
    /// - It never stops a long or endless sequence. An iterator that never ends makes the walk
    ///   hang. ValidatorHardeningTests says that lazy or infinite sequences of objects need a
    ///   separate decision. A hang cannot be a test, because it stops the test run, so there is
    ///   no spec for it here. A collection item behaves the same as a property after the fix.
    ///
    /// A fix that enumerates items therefore changes what a model can do to the caller. A model
    /// that passes on 2.3.3 can throw on 2.4, if one of its items is a collection that throws
    /// when it is enumerated. Only the cases below are realistic. A collection that throws on
    /// enumeration is a broken collection, but a default struct is a common way to get one.
    /// </summary>
    public class NestedCollectionEdgeCaseTests
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

        /// <summary>
        /// A collection that throws when it is enumerated. It is not sealed, so the validator cannot
        /// tell from the type that it has nothing to validate (see IsLeafType), and it enumerates it.
        /// </summary>
        public class ThrowingBag : IEnumerable<Leaf>
        {
            public IEnumerator<Leaf> GetEnumerator() => throw new InvalidOperationException("enumeration failed");

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// Case 1: an item whose enumeration throws.
        /// Reasoning:
        /// - The validator does not catch exceptions anywhere, and the guard below shows that a
        ///   property that holds this collection already throws. An item should do the same, so one
        ///   rule covers both and the caller handles one kind of failure.
        /// - Catching would turn a broken model into "valid" or "invalid", and neither is true. The
        ///   caller could not tell a bad model from a bad collection.
        /// - The cost is that a model which passes on 2.3.3 can throw on 2.4, because 2.3.3 never
        ///   enumerates the item. This is a rare shape: a collection that throws on enumeration.
        /// Recommendation: let it propagate. Add a line to the changelog entry.
        /// </summary>
        public class ItemThatThrowsWhenEnumerated
        {
            // Guard. This passes today, and shows the rule that the spec below extends to an item.
            [Fact]
            public void Property_that_throws_when_enumerated_propagates_today()
            {
                var ex = Record.Exception(() => Run(new Holder<ThrowingBag> { Value = new ThrowingBag() }));

                Assert.IsType<InvalidOperationException>(ex);
            }

            [Fact(Skip = "Open. Whether an item that throws when enumerated should propagate like a property is not decided.")]
            public void Item_that_throws_when_enumerated_propagates()
            {
                var ex = Record.Exception(() => Run(new Holder<List<ThrowingBag>> { Value = new List<ThrowingBag> { new ThrowingBag() } }));

                Assert.IsType<InvalidOperationException>(ex);
            }

            // Passes on 2.3.3, because nothing enumerates the item. If the ruling is to propagate,
            // this guard is deleted with the spec above unskipped, so the change is deliberate.
            [Fact]
            public void Item_that_throws_when_enumerated_is_not_enumerated_today()
            {
                var (valid, errors) = Run(new Holder<List<ThrowingBag>> { Value = new List<ThrowingBag> { new ThrowingBag() } });

                Assert.True(valid);
                Assert.Empty(errors);
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Case 2: an item that is a default ImmutableArray.
        /// An ImmutableArray is a struct that wraps an array. default(ImmutableArray) wraps null,
        /// and enumerating it throws InvalidOperationException. A model gets one by accident: a
        /// field that nobody set, a deserializer that skipped a missing JSON property, or a
        /// constructor that left it out. It is the common way to meet a collection that throws.
        /// See: https://learn.microsoft.com/dotnet/api/system.collections.immutable.immutablearray-1.isdefault
        /// Reasoning:
        /// - A default array holds no objects, so "valid" is the correct answer, and the answer on
        ///   2.3.3. A fix that makes it throw turns a passing model into a crash for a reason the
        ///   caller did not cause and cannot see in the model.
        /// - The same array held in a property is not walked at all (StructsAreNotWalked), so it
        ///   passes today and after the fix. Only an item would throw. That difference is a trap.
        /// - The fix can skip a value that is a default ImmutableArray. It checks IsDefault, which
        ///   needs the type, so this is a special case for one framework type. I know of no other
        ///   framework collection that throws when it is default. Other struct collections are
        ///   the caller's own types, and Case 1 covers them.
        /// Recommendation: skip a default ImmutableArray, and validate a non-default one.
        /// The alternative is Case 1 for this type too. It is less work, and it breaks models.
        /// </summary>
        public class DefaultImmutableArrayItem
        {
            [Fact(Skip = "Open. A default ImmutableArray item must not throw, but the fix is not decided.")]
            public void Default_immutable_array_item_is_treated_as_empty()
            {
                var (valid, errors) = Run(new Holder<List<ImmutableArray<Leaf>>>
                {
                    Value = new List<ImmutableArray<Leaf>> { default },
                });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact(Skip = "Open. A default ImmutableArray item must not throw, but the fix is not decided.")]
            public void Default_immutable_array_in_an_object_array_is_treated_as_empty()
            {
                // The item is boxed as an object, which is how the validator sees it in an object[].
                var (valid, errors) = Run(new Holder<object[]> { Value = new object[] { default(ImmutableArray<Leaf>) } });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            // Guard. The item that is not default is validated by the spec in OddShapeTests, and a
            // default array held in a property passes today and must keep passing.
            [Fact]
            public void Default_immutable_array_in_a_property_is_not_walked()
            {
                var (valid, errors) = Run(new Holder<ImmutableArray<Leaf>> { Value = default });

                Assert.True(valid);
                Assert.Empty(errors);
            }
        }
#endif
    }
}
