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
        /// Problem: real validation errors are silently dropped. A caller can bypass validation
        /// by sending equal-looking invalid items. A custom GetHashCode also runs on
        /// untrusted objects.
        /// Proposed fix: add a small internal reference-equality comparer (netstandard2.0 has no
        /// ReferenceEqualityComparer). It uses ReferenceEquals and RuntimeHelpers.GetHashCode.
        /// Pass it to both `new HashSet&lt;object&gt;()` calls in RecursiveDataAnnotationValidator.
        /// Behavior change: results for graphs with equal-but-distinct objects now include the
        /// previously dropped errors. That is a bug fix, but a changelog entry is needed.
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

            [Fact]
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

            [Fact]
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
        /// The validator finds properties with GetProperties(), then re-reads each value by name
        /// through obj.GetPropertyValue(name), which calls GetProperty(name). When a derived class
        /// hides a base property with `new` and gives it a different type, the name matches two
        /// properties and GetProperty throws AmbiguousMatchException. (A hiding property of the
        /// same type does not throw.)
        /// Problem: validation throws for a valid model shape. The result is a failed request or a
        /// crash instead of validation errors.
        /// Proposed fix: read the value from the PropertyInfo already in hand,
        /// `property.GetValue(obj, null)`, instead of looking it up by name again.
        /// Behavior change: none for models that already validate. Models that threw now validate.
        /// Both the base and the derived property are walked, and both report as "Nested.x".
        /// GetPropertyValue becomes unused. Remove it with its tests, or keep it.
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
        /// Problem: a model with a Type property cannot be validated. The walk is also slow and
        /// exposes internals of framework objects.
        /// Proposed fix (needs a decision): do not recurse into objects whose type lives in a
        /// framework namespace (System.* or Microsoft.*). Alternatives: a deny list (Type,
        /// MemberInfo, Assembly, Stream, Delegate), or catching getter exceptions.
        /// The namespace rule is the simplest. It also covers types we did not list, such as
        /// IFormFile.
        /// Behavior change: framework types are no longer validated. This only matters if someone
        /// relies on attributes inside framework objects, which is unlikely. Collections are
        /// handled separately and are not affected.
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

            [Fact]
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
        /// Proposed fix: skip a collection when its element type is a primitive, an enum,
        /// string or decimal. Find the element type from IEnumerable&lt;T&gt;, or from the array
        /// element type. The decision is by type, so no enumeration happens.
        /// Behavior change: items of these types are no longer passed to the validator. They carry
        /// no attributes, so results do not change. Collections of structs or objects are not
        /// skipped, because their items can have attributes.
        /// Not solved here: lazy or infinite sequences of objects, and lazy queryables that hit
        /// a database. Those need a separate decision.
        /// </summary>
        public class PrimitiveCollections
        {
            /// <summary>Records whether anything enumerated it.</summary>
            public class CountingSequence : IEnumerable<int>
            {
                public int EnumerationCount { get; private set; }

                public IEnumerator<int> GetEnumerator()
                {
                    EnumerationCount++;
                    return Enumerable.Range(0, 3).GetEnumerator();
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class SequenceHolder
            {
                public CountingSequence Numbers { get; set; } = new CountingSequence();
            }

            public class ByteArrayHolder
            {
                public byte[] Payload { get; set; } = new byte[1024];
            }

            [Fact]
            public void Collections_of_primitives_are_not_enumerated()
            {
                var model = new SequenceHolder();

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);

                Assert.True(valid);
                Assert.Equal(0, model.Numbers.EnumerationCount);
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
