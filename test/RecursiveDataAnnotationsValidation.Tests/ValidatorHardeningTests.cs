using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
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
        /// validates each one.
        /// Problem: wasted CPU and memory on large payloads. It is a denial-of-service
        /// vector for model-bound input.
        ///
        /// How an item is validated. The validator enumerates the collection through the
        /// non-generic IEnumerable, so each item arrives as an object. A value type such as int is
        /// boxed: copied into a new heap object. The validator then calls
        /// Validator.TryValidateObject on that object. It checks two kinds of attributes:
        /// - Type-level attributes, declared on the item's type itself.
        /// - Property-level attributes, declared on the item's properties.
        /// Validator reads both through TypeDescriptor, not plain reflection. TypeDescriptor
        /// returns the attributes in the source code plus any added at runtime with
        /// TypeDescriptor.AddAttributes.
        /// See: https://learn.microsoft.com/dotnet/csharp/programming-guide/types/boxing-and-unboxing
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.typedescriptor.getattributes
        /// See (Validator's attribute lookup): https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationAttributeStore.cs
        ///
        /// Where attributes can come from. Built-in types such as int, string and DateTime have
        /// no validation attributes in their source, and you cannot edit that source. Two routes
        /// remain, and both are validated today:
        /// - An enum is your own type. A custom ValidationAttribute declared with
        ///   [AttributeUsage(AttributeTargets.Enum)] can go on the enum declaration. It then runs
        ///   for every enum item in a collection.
        /// - TypeDescriptor.AddAttributes can attach an attribute to any type at runtime,
        ///   including int.
        /// See: https://learn.microsoft.com/dotnet/api/system.attributetargets
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.typedescriptor.addattributes
        /// To limit the values inside a collection, put the attribute on the collection property
        /// instead, for example [MaxLength] or a custom attribute that checks each item. The
        /// parent object's validation runs that attribute, so this skip does not affect it.
        ///
        /// Fix: skip a collection when every element type it declares is a leaf type: a type
        /// where validating an item can never produce a result. The rule is a set of checks on
        /// the type, not a list of type names, so it also covers types the library cannot name,
        /// such as DateOnly. TypeExtensionsTests lists the four checks.
        /// The element type comes from the array element type, or from each IEnumerable&lt;T&gt; the
        /// collection implements. A Nullable&lt;T&gt; element is checked as T, because a boxed
        /// Nullable&lt;T&gt; is either null or a boxed T. Each Dictionary item is a KeyValuePair, which
        /// passes the checks when its Key and Value are a string or a value type.
        /// The decision is by type, so no enumeration happens.
        /// See: https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1
        /// See: https://learn.microsoft.com/dotnet/api/system.nullable.getunderlyingtype
        /// See: https://learn.microsoft.com/dotnet/api/system.collections.generic.keyvaluepair-2
        ///
        /// Behavior change: items with nothing to validate are no longer passed to the validator,
        /// so the validation results do not change. No property getter on a skipped item would
        /// have run either: Validator reads a property's value only when the property has a
        /// validation attribute (GetPropertyValues), and the walk reads only reference-type
        /// properties.
        /// See: https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs
        /// A lazy sequence of leaf types, such as a LINQ query or an IQueryable&lt;int&gt;, is no
        /// longer run. LINQ queries use deferred execution: the query body runs only when
        /// something enumerates it. So an exception the query throws while enumerating no longer
        /// surfaces during validation.
        /// See: https://learn.microsoft.com/dotnet/standard/linq/deferred-execution-lazy-evaluation
        ///
        /// Accepted gap: a type whose non-generic enumerator yields different items than its
        /// IEnumerable&lt;T&gt; breaks the IEnumerable&lt;T&gt; contract. It is skipped by its declared type.
        /// Known gap, not changed here: a type-level error on an item, such as the enum attribute
        /// above, has no member names. The validator builds each path by prefixing the item's
        /// member names, so that error is reported with no path at all.
        /// Not solved here: lazy or infinite sequences of objects, and lazy queryables of objects
        /// that hit a database. Those need a separate decision.
        /// </summary>
        public class PrimitiveCollections
        {
            public enum Color { Red, Green }

            /// <summary>
            /// Fails for a value that is not a named member of its enum, such as (CheckedColor)99.
            /// A cast from an int to an enum never checks that the value is defined.
            /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum
            /// </summary>
            [AttributeUsage(AttributeTargets.Enum)]
            public class DefinedValueAttribute : ValidationAttribute
            {
                public override bool IsValid(object value) =>
                    value == null || Enum.IsDefined(value.GetType(), value);
            }

            [DefinedValue]
            public enum CheckedColor { Red, Green }

            // No attribute in source. A test adds one at runtime with TypeDescriptor.
            public enum RuntimeCheckedColor { Red, Green }

            // No attribute in source. A test adds one after a first validation.
            public enum LateCheckedColor { Red, Green }

            public struct Point
            {
                [Range(0, 10)]
                public int X { get; set; }
            }

            // No attributes, no IValidatableObject, no reference-type properties.
            public struct PlainPoint
            {
                public int X { get; set; }
            }

            public struct SelfValidatingPoint : IValidatableObject
            {
                public int X { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    if (X > 10) yield return new ValidationResult("X must be 10 or less.", new[] { nameof(X) });
                }
            }

            // No properties, so no checks fail on the type itself. It is not sealed, though.
            public class Shape
            {
            }

            public class Circle : Shape
            {
                [Required]
                public string Name { get; set; }
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
                AssertNotEnumerated(Color.Red, Color.Green);
                AssertNotEnumerated("a", "b");
                AssertNotEnumerated(1.5m);
                AssertNotEnumerated(DateTime.UtcNow);
                AssertNotEnumerated(DateTimeOffset.UtcNow);
                AssertNotEnumerated(TimeSpan.FromSeconds(1));
                AssertNotEnumerated(Guid.NewGuid());
                AssertNotEnumerated<int?>(1, null);
                AssertNotEnumerated(new KeyValuePair<string, int>("a", 1));
            }

            private static void AssertNotEnumerated<T>(params T[] items)
            {
                var (valid, results, enumerationCount) = Validate(items);

                Assert.True(valid);
                Assert.Empty(results);
                Assert.Equal(0, enumerationCount);
            }

            public class DictionaryHolder
            {
                public Dictionary<string, Child> Map { get; set; }
            }

            // Guard. Each dictionary item is a boxed KeyValuePair, and its Value is still walked.
            [Fact]
            public void Dictionary_of_objects_is_still_validated()
            {
                var model = new DictionaryHolder { Map = new Dictionary<string, Child> { ["a"] = new Child() } };

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);

                Assert.False(valid);
                Assert.Contains(results, r => r.MemberNames.Contains("Map[0].Value.Name"));
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

            // Guard. An enum is a leaf type, but this one has a type-level validation attribute
            // in its source. Each item must still be validated. The error has no member names,
            // so this test checks the count only (see the class summary).
            [Fact]
            public void Collections_of_enums_with_a_validation_attribute_are_still_validated()
            {
                var (valid, results, enumerationCount) = Validate(CheckedColor.Red, (CheckedColor)99);

                Assert.False(valid);
                Assert.Single(results);
                Assert.Equal(1, enumerationCount);
            }

            // Guard. The same check for an attribute attached at runtime with
            // TypeDescriptor.AddAttributes. Validator sees these attributes too.
            // This test uses its own enum, for two reasons:
            // - TypeDescriptor state is global to the process. Adding an attribute to a shared
            //   type such as int would affect other tests that xUnit runs in parallel.
            // - Validator caches each type's attributes the first time it validates that type,
            //   and never refreshes the cache. If another test had already validated this type,
            //   Validator would ignore the new attribute and this test would pass for the
            //   wrong reason.
            // RemoveProvider undoes the AddAttributes call, so the type is clean afterwards.
            // See: https://learn.microsoft.com/dotnet/api/system.componentmodel.typedescriptor.addattributes
            // See: https://learn.microsoft.com/dotnet/api/system.componentmodel.typedescriptor.removeprovider
            // See: https://xunit.net/docs/running-tests-in-parallel
            [Fact]
            public void Collections_of_a_type_with_a_runtime_validation_attribute_are_still_validated()
            {
                var provider = TypeDescriptor.AddAttributes(typeof(RuntimeCheckedColor), new DefinedValueAttribute());
                try
                {
                    var (valid, results, enumerationCount) = Validate(RuntimeCheckedColor.Red, (RuntimeCheckedColor)99);

                    Assert.False(valid);
                    Assert.Single(results);
                    Assert.Equal(1, enumerationCount);
                }
                finally
                {
                    TypeDescriptor.RemoveProvider(provider, typeof(RuntimeCheckedColor));
                }
            }

            // Guard. The validator caches whether a type has validation attributes. This test
            // checks that the cache notices an attribute added after the first validation.
            // TypeDescriptor.AddAttributes raises the TypeDescriptor.Refreshed event, and the
            // validator listens for it. The first validation must skip the collection
            // (EnumerationCount 0). Otherwise Validator would cache this type with no attributes
            // and the second validation could not fail, whatever the skip logic did.
            // See: https://learn.microsoft.com/dotnet/api/system.componentmodel.typedescriptor.refreshed
            [Fact]
            public void Validation_attribute_added_after_first_validation_is_seen()
            {
                var before = Validate(LateCheckedColor.Red, (LateCheckedColor)99);
                Assert.True(before.Valid);
                Assert.Equal(0, before.EnumerationCount);

                var provider = TypeDescriptor.AddAttributes(typeof(LateCheckedColor), new DefinedValueAttribute());
                try
                {
                    var (valid, results, enumerationCount) = Validate(LateCheckedColor.Red, (LateCheckedColor)99);

                    Assert.False(valid);
                    Assert.Single(results);
                    Assert.Equal(1, enumerationCount);
                }
                finally
                {
                    TypeDescriptor.RemoveProvider(provider, typeof(LateCheckedColor));
                }
            }

            // A user struct with nothing to validate passes the same checks as a built-in type.
            [Fact]
            public void Collections_of_structs_with_nothing_to_validate_are_not_enumerated()
            {
                AssertNotEnumerated(new PlainPoint { X = 99 });
            }

            // Guard. Validator calls Validate() on any item that implements IValidatableObject,
            // including a boxed struct, so these items are still validated.
            // See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.ivalidatableobject
            [Fact]
            public void Collections_of_self_validating_structs_are_still_validated()
            {
                var (valid, results, enumerationCount) = Validate(new SelfValidatingPoint { X = 11 });

                Assert.False(valid);
                Assert.Contains(results, r => r.MemberNames.Contains("Items[0].X"));
                Assert.Equal(1, enumerationCount);
            }

            // Guard. Shape has nothing to validate, but it is not sealed. A collection declared
            // with Shape items can hold a Circle, which has its own attributes. Only the runtime
            // type of each item shows that, so the collection must be enumerated.
            // See: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/sealed
            [Fact]
            public void Collections_of_an_unsealed_type_still_validate_derived_items()
            {
                var (valid, results, enumerationCount) = Validate<Shape>(new Circle());

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
