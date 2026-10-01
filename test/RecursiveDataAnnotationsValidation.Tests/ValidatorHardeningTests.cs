using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// Tests for the validator hardening items from the OWASP sweep (proposal 3).
    /// Each nested class covers one item. Its summary gives the problem and the proposed fix.
    /// Some tests guard behavior that a fix must not break. Their comments say why.
    /// Not covered:
    /// - A max-depth limit. A deep acyclic graph causes an uncatchable StackOverflowException
    ///   that would kill the test host, so it needs a design decision first.
    /// - Lazy or infinite sequences, and user getters that throw. The desired behavior
    ///   (skip, report or propagate) is not decided yet.
    /// </summary>
    public class ValidatorHardeningTests
    {
        public class Child
        {
            [Required]
            public string Name { get; set; }
        }

        /// <summary>
        /// Null arguments.
        /// Each public overload throws ArgumentNullException for a null obj or a null
        /// validationContext, with the matching parameter name.
        /// Before this guard, a null validationContext threw NullReferenceException (at
        /// validationContext.Items), and a null obj threw ArgumentNullException from the
        /// framework's ValidationContext with param name "instance".
        /// </summary>
        public class NullGuards
        {
            [Fact]
            public void Null_object_throws_ArgumentNullException_with_validation_context()
            {
                var ex = Assert.Throws<ArgumentNullException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(
                        null,
                        new ValidationContext(new object()),
                        new List<ValidationResult>()));
                Assert.Equal("obj", ex.ParamName);
            }

            [Fact]
            public void Null_object_throws_ArgumentNullException_with_context_items()
            {
                var ex = Assert.Throws<ArgumentNullException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(
                        null,
                        new List<ValidationResult>()));
                Assert.Equal("obj", ex.ParamName);
            }

            [Fact]
            public void Null_validation_context_throws_ArgumentNullException()
            {
                var ex = Assert.Throws<ArgumentNullException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(
                        new Child { Name = "x" },
                        (ValidationContext)null,
                        new List<ValidationResult>()));
                Assert.Equal("validationContext", ex.ParamName);
            }

            [Fact]
            public async Task Null_object_throws_ArgumentNullException_async_with_validation_context()
            {
                var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursiveAsync(
                        null,
                        new ValidationContext(new object()),
                        new List<ValidationResult>()));
                Assert.Equal("obj", ex.ParamName);
            }

            [Fact]
            public async Task Null_object_throws_ArgumentNullException_async_with_context_items()
            {
                var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursiveAsync(
                        null,
                        new List<ValidationResult>()));
                Assert.Equal("obj", ex.ParamName);
            }

            [Fact]
            public async Task Null_validation_context_throws_ArgumentNullException_async()
            {
                var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursiveAsync(
                        new Child { Name = "x" },
                        (ValidationContext)null,
                        new List<ValidationResult>()));
                Assert.Equal("validationContext", ex.ParamName);
            }
        }

        /// <summary>
        /// Cycle detection uses HashSet&lt;object&gt; with the default comparer, which calls
        /// Equals and GetHashCode. Two different objects that compare as equal count as
        /// "already validated", so the second one is never checked.
        /// Records (value equality) and classes with a custom Equals both trigger this.
        /// Problem: with an Equals keyed on an Id, a valid item followed by an invalid item with
        /// the same Id returns valid=true with no errors. The invalid item bypasses validation.
        /// Records cannot bypass validation this way, because equal records have the same values
        /// and so the same validity. They only lose the error paths for the duplicates.
        /// A custom GetHashCode also runs on untrusted objects.
        /// Proposed fix: add a small internal reference-equality comparer (netstandard2.0 has no
        /// ReferenceEqualityComparer). It uses ReferenceEquals and RuntimeHelpers.GetHashCode.
        /// Pass it to both `new HashSet&lt;object&gt;()` calls in RecursiveDataAnnotationValidator.
        /// Order: land this with or after the primitive-collection skip (see PrimitiveCollections).
        /// Today, boxed primitives in a collection are de-duplicated by value, so a byte[] of
        /// zeros validates one item. With reference equality, every boxed item is validated and
        /// kept in the set. For 1M items that is about 10 times slower.
        /// Risk: a computed property that returns a new, equal instance on each read, such as
        /// `Point Origin => new Point(0, 0)` on a record, stops today only because of value
        /// equality. With reference equality it recurses until the stack overflows. This needs
        /// a decision together with the max-depth item.
        /// Behavior change: results for graphs with equal-but-distinct objects now include the
        /// previously dropped errors. Models that passed because of the bypass now fail.
        /// That is a bug fix, but it needs a changelog entry.
        /// </summary>
        public class ReferenceEquality
        {
            public record ChildRecord
            {
                [Required]
                public string Name { get; init; }
            }

            public class AlwaysEqualChild
            {
                [Required]
                public string Name { get; set; }

                public override bool Equals(object obj) => obj is AlwaysEqualChild;
                public override int GetHashCode() => 0;
            }

            public class RecordListModel
            {
                public List<ChildRecord> Children { get; set; }
            }

            public class CustomEqualsListModel
            {
                public List<AlwaysEqualChild> Children { get; set; }
            }

            [Fact(Skip = "Not fixed yet. Needs the reference-equality comparer, which must land with or after the primitive-collection skip.")]
            public void Equal_but_distinct_records_are_each_validated()
            {
                // Records with equal values are Equals() to each other but are separate instances.
                var model = new RecordListModel
                {
                    Children = new List<ChildRecord> { new ChildRecord(), new ChildRecord() }
                };
                Assert.Equal(model.Children[0], model.Children[1]);
                Assert.NotSame(model.Children[0], model.Children[1]);

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);

                Assert.False(valid);
                var members = results.SelectMany(r => r.MemberNames).ToList();
                Assert.Contains("Children[0].Name", members);
                Assert.Contains("Children[1].Name", members);
            }

            [Fact(Skip = "Not fixed yet. Needs the reference-equality comparer, which must land with or after the primitive-collection skip.")]
            public void Objects_with_custom_Equals_are_each_validated()
            {
                var model = new CustomEqualsListModel
                {
                    Children = new List<AlwaysEqualChild> { new AlwaysEqualChild(), new AlwaysEqualChild() }
                };

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);

                Assert.False(valid);
                var members = results.SelectMany(r => r.MemberNames).ToList();
                Assert.Contains("Children[0].Name", members);
                Assert.Contains("Children[1].Name", members);
            }

            // Guard: the reference-equality fix must keep de-duplicating the same instance.
            // This is what stops cycles and repeated errors for shared objects.
            [Fact]
            public void Same_instance_referenced_twice_is_still_validated_once()
            {
                var shared = new Child();
                var model = new SharedChildModel { First = shared, Second = shared };

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);

                Assert.False(valid);
                Assert.Single(results);
            }

            public class SharedChildModel
            {
                public Child First { get; set; }
                public Child Second { get; set; }
            }
        }

        /// <summary>
        /// A derived class hides a base property with `new` and gives it a different type.
        /// GetProperties() then returns both properties. The validator used to re-read each value
        /// by name with GetProperty(name), which threw AmbiguousMatchException. (A hiding property
        /// of the same type is returned only once, so it never threw.)
        /// The validator now reads the value from the PropertyInfo it already has. Both the base
        /// and the derived property are walked, and both report as "Nested.x".
        /// </summary>
        public class HiddenProperties
        {
            public class BaseModel
            {
                public object Nested { get; set; }
            }

            public class DerivedModel : BaseModel
            {
                public new Child Nested { get; set; }
            }

            [Fact]
            public void Property_hidden_with_new_does_not_throw()
            {
                var model = new DerivedModel
                {
                    Nested = new Child(),
                };
                ((BaseModel)model).Nested = new Child();

                var results = new List<ValidationResult>();
                var valid = false;
                var ex = Record.Exception(() =>
                    valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results));

                Assert.Null(ex);
                Assert.False(valid);
                Assert.Contains(results, r => r.MemberNames.Contains("Nested.Name"));
            }
        }

        /// <summary>
        /// The validator walks into every reference-type property, including framework objects
        /// such as Type, Stream or IFormFile. It reads all of their reference-type properties.
        /// Some of those getters throw. For example Type.DeclaringMethod throws
        /// InvalidOperationException. Framework objects also carry no DataAnnotations.
        /// Problem: a model with a Type property cannot be validated. The walk is also slow.
        /// Rejected fix: skipping every type in a System.* or Microsoft.* namespace. User objects
        /// inside framework wrappers are validated today, and that rule would silently stop it.
        /// A Tuple&lt;Child, int&gt; property reports "Pair.Item1.Name". A Dictionary&lt;string, Child&gt;
        /// reports "Map[0].Value.Name", because each item is a boxed KeyValuePair, which lives in
        /// System.Collections.Generic.
        /// Proposed fix (needs a decision): a narrow deny list (MemberInfo, which covers Type,
        /// plus Assembly, Module and Delegate), checked only for non-collection property values.
        /// Add guard tests for the tuple and dictionary cases first.
        /// Behavior change: values of the denied types are no longer walked. They carry no
        /// DataAnnotations, and a Type property throws today, so no caller relies on the walk.
        /// </summary>
        public class FrameworkTypes
        {
            public class TypeHolder
            {
                public Type ModelType { get; set; } = typeof(string);
            }

            public class StreamHolder
            {
                public Stream Content { get; set; } = new GZipStream(new MemoryStream(), CompressionMode.Compress);
            }

            [Fact(Skip = "Not fixed yet. The framework-type skip rule needs a decision.")]
            public void Type_property_does_not_throw()
            {
                var results = new List<ValidationResult>();
                var valid = false;
                var ex = Record.Exception(() =>
                    valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(new TypeHolder(), results));

                Assert.Null(ex);
                Assert.True(valid);
                Assert.Empty(results);
            }

            // Guard. A Stream does not fail, because the validator only reads reference-type
            // properties, and Length and Position are long. A GZipStream still has reference-type
            // properties (BaseStream) that get walked. This test keeps a Stream from failing after
            // a fix. It does not prove the walk is skipped.
            [Fact]
            public void Stream_property_does_not_throw()
            {
                var holder = new StreamHolder();
                var results = new List<ValidationResult>();
                var valid = false;
                var ex = Record.Exception(() =>
                    valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(holder, results));

                Assert.Null(ex);
                Assert.True(valid);
                Assert.Empty(results);
            }
        }

        /// <summary>
        /// Every IEnumerable property is fully enumerated, and each item is run through
        /// validation. For a byte[] with a million bytes, that boxes a million objects and
        /// validates each one. Items such as ints and strings have no DataAnnotations to check.
        /// Problem: wasted CPU and memory on large payloads. It is a denial-of-service
        /// vector for model-bound input.
        /// Fix: skip a collection when every element type it can yield is a primitive, an enum,
        /// string, decimal, DateTime, DateTimeOffset, TimeSpan or Guid, or a Nullable of one of
        /// those. The element type comes from the array element type, or from each IEnumerable&lt;T&gt;
        /// the collection implements. The decision is by type, so no enumeration happens.
        /// Behavior change: items of these types are no longer passed to the validator. They carry
        /// no attributes, so results do not change. Collections of structs or objects are not
        /// skipped, because their items can have attributes.
        /// Not solved here: lazy or infinite sequences of objects, and lazy queryables that hit
        /// a database. Those need a separate decision.
        /// </summary>
        public class PrimitiveCollections
        {
            public enum Color { Red, Green }

            public struct Point
            {
                [Range(0, 10)]
                public int X { get; set; }
            }

            /// <summary>Records whether anything enumerated it.</summary>
            public class CountingSequence<T> : IEnumerable<T>
            {
                private readonly T[] _items;

                public CountingSequence(params T[] items) => _items = items;

                public int EnumerationCount { get; private set; }

                public IEnumerator<T> GetEnumerator()
                {
                    EnumerationCount++;
                    return ((IEnumerable<T>)_items).GetEnumerator();
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class SequenceHolder<T>
            {
                public CountingSequence<T> Items { get; set; }
            }

            public class ByteArrayHolder
            {
                public byte[] Payload { get; set; } = new byte[1024];
            }

            private static (bool Valid, List<ValidationResult> Results, int EnumerationCount) Validate<T>(params T[] items)
            {
                var model = new SequenceHolder<T> { Items = new CountingSequence<T>(items) };
                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
                return (valid, results, model.Items.EnumerationCount);
            }

            [Fact]
            public void Collections_of_primitives_are_not_enumerated()
            {
                var (valid, results, enumerationCount) = Validate(0, 1, 2);

                Assert.True(valid);
                Assert.Empty(results);
                Assert.Equal(0, enumerationCount);
            }

            [Fact]
            public void Collections_of_other_leaf_types_are_not_enumerated()
            {
                Assert.Equal(0, Validate(Color.Red, Color.Green).EnumerationCount);
                Assert.Equal(0, Validate("a", "b").EnumerationCount);
                Assert.Equal(0, Validate(1.5m).EnumerationCount);
                Assert.Equal(0, Validate(DateTime.UtcNow).EnumerationCount);
                Assert.Equal(0, Validate(DateTimeOffset.UtcNow).EnumerationCount);
                Assert.Equal(0, Validate(TimeSpan.FromSeconds(1)).EnumerationCount);
                Assert.Equal(0, Validate(Guid.NewGuid()).EnumerationCount);
                Assert.Equal(0, Validate<int?>(1, null).EnumerationCount);
            }

            // Guard. Items of a reference type can carry attributes, so they are still validated.
            [Fact]
            public void Collections_of_objects_are_still_enumerated()
            {
                var (valid, results, enumerationCount) = Validate(new Child());

                Assert.False(valid);
                Assert.Contains(results, r => r.MemberNames.Contains("Items[0].Name"));
                Assert.Equal(1, enumerationCount);
            }

            // Guard. Items of a user struct can carry attributes, so they are still validated.
            [Fact]
            public void Collections_of_structs_are_still_enumerated()
            {
                var (valid, results, enumerationCount) = Validate(new Point { X = 11 });

                Assert.False(valid);
                Assert.Contains(results, r => r.MemberNames.Contains("Items[0].X"));
                Assert.Equal(1, enumerationCount);
            }

            [Fact]
            public void Byte_array_property_validates()
            {
                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(new ByteArrayHolder(), results);

                Assert.True(valid);
                Assert.Empty(results);
            }
        }
    }
}
