using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Attributes;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// What the caller sees when something the validator runs throws. The validator does not catch
    /// anything. The exception leaves TryValidateObjectRecursive, and the answer is not "invalid".
    /// A caller that wants "invalid" for a broken model has to catch the exception itself.
    ///
    /// The type that reaches the caller depends on who threw:
    /// - A property getter that the walk reads, such as a model that computes a child on each read.
    ///   The walk reads it with PropertyInfo.GetValue, which wraps any exception from the getter in
    ///   a TargetInvocationException. The original exception is its InnerException.
    /// - An IValidatableObject.Validate or a ValidationAttribute.IsValid. The framework's Validator
    ///   calls these directly, so the exception arrives as it was thrown.
    /// - A getter that Validator itself reads, because an attribute sits on the property.
    ///   Validator reads it through TypeDescriptor, which also wraps the exception in a
    ///   TargetInvocationException.
    /// The async methods run the same code on a thread-pool thread. The Task they return faults
    /// with the same exception, and await throws it again.
    ///
    /// [SkipRecursiveValidation] on a property keeps the walk from reading it. That is the way to
    /// stop a getter that throws, such as one that only works on a request thread.
    /// A property of type string or of a value type is never read by the walk, because it cannot
    /// hold an object with its own attributes.
    ///
    /// The tests only use the public API, so they also pass against the validator of v2.2.0.
    /// See: https://learn.microsoft.com/dotnet/api/system.reflection.propertyinfo.getvalue
    /// See: https://learn.microsoft.com/dotnet/api/system.reflection.targetinvocationexception
    /// </summary>
    public class ThrowingMemberTests
    {
        public class Leaf
        {
            [Required(ErrorMessage = "Name is required")]
            public string Name { get; set; }
        }

        /// <summary>A getter that throws. The type of the property is a class, so the walk reads it.</summary>
        public class BrokenGetter
        {
            public Leaf Child => throw new InvalidOperationException("getter failed");
        }

        public class HoldsBroken
        {
            public BrokenGetter Inner { get; set; } = new BrokenGetter();
        }

        public class HoldsBrokenItems
        {
            public List<BrokenGetter> Items { get; set; } = new List<BrokenGetter> { new BrokenGetter() };
        }

        private static bool Run(object model, out List<string> errors)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            errors = ResultText.Describe(results);
            return valid;
        }

        private static void AssertGetterFailure(TargetInvocationException thrown)
        {
            var inner = Assert.IsType<InvalidOperationException>(thrown.InnerException);
            Assert.Equal("getter failed", inner.Message);
        }

        /// <summary>A getter the walk reads throws a TargetInvocationException, wherever the object sits.</summary>
        public class GetterTheWalkReads
        {
            [Fact]
            public void On_the_root_object()
            {
                AssertGetterFailure(Assert.Throws<TargetInvocationException>(() => Run(new BrokenGetter(), out _)));
            }

            [Fact]
            public void On_a_nested_object()
            {
                AssertGetterFailure(Assert.Throws<TargetInvocationException>(() => Run(new HoldsBroken(), out _)));
            }

            [Fact]
            public void On_a_collection_item()
            {
                AssertGetterFailure(Assert.Throws<TargetInvocationException>(() => Run(new HoldsBrokenItems(), out _)));
            }

            [Fact]
            public async Task In_the_async_method()
            {
                var thrown = await Assert.ThrowsAsync<TargetInvocationException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursiveAsync(
                        new HoldsBroken(), new List<ValidationResult>()));

                AssertGetterFailure(thrown);
            }

            [Fact]
            public void Is_not_reported_as_an_invalid_result()
            {
                // The caller never gets false and a results list that explains it. A caller that
                // wants that has to catch the exception.
                var results = new List<ValidationResult>();

                Assert.Throws<TargetInvocationException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(new HoldsBroken(), results));

                Assert.Empty(results);
            }
        }

        public class SkippedGetter
        {
            [SkipRecursiveValidation]
            public Leaf Child => throw new InvalidOperationException("getter failed");

            [Required(ErrorMessage = "Label is required")]
            public string Label { get; set; }
        }

        // A string or a value type is never read by the walk, so a getter that throws goes unnoticed.
        // Validator would read it only if an attribute were on the property.
        public class UnwalkedGetters
        {
            public string Text => throw new InvalidOperationException("getter failed");

            public int Number => throw new InvalidOperationException("getter failed");

            public Guid Id => throw new InvalidOperationException("getter failed");

            [Required(ErrorMessage = "Label is required")]
            public string Label { get; set; }
        }

        /// <summary>The ways to keep a getter that throws out of the walk.</summary>
        public class GetterTheWalkSkips
        {
            [Fact]
            public void Property_marked_with_the_skip_attribute_is_not_read()
            {
                Assert.False(Run(new SkippedGetter(), out var errors));
                Assert.Equal(ResultText.Expect("Label | Label is required"), errors);

                Assert.True(Run(new SkippedGetter { Label = "ok" }, out errors));
                Assert.Empty(errors);
            }

            [Fact]
            public void Property_of_a_string_or_value_type_is_not_read()
            {
                Assert.False(Run(new UnwalkedGetters(), out var errors));
                Assert.Equal(ResultText.Expect("Label | Label is required"), errors);

                Assert.True(Run(new UnwalkedGetters { Label = "ok" }, out errors));
                Assert.Empty(errors);
            }
        }

        public class AttributeOnBrokenGetter
        {
            [Required(ErrorMessage = "Text is required")]
            public string Text => throw new InvalidOperationException("getter failed");
        }

        public class SelfValidatingThatThrows : IValidatableObject
        {
            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
                throw new InvalidOperationException("validate failed");
        }

        public class AlwaysThrowsAttribute : ValidationAttribute
        {
            public override bool IsValid(object value) => throw new InvalidOperationException("attribute failed");
        }

        public class AttributeThatThrows
        {
            [AlwaysThrows]
            public string Name { get; set; }
        }

        public class HoldsSelfValidatingThatThrows
        {
            public List<SelfValidatingThatThrows> Items { get; set; } = new List<SelfValidatingThatThrows> { new SelfValidatingThatThrows() };
        }

        /// <summary>
        /// Code that the framework's Validator runs. Its exceptions are not wrapped, except that
        /// a getter Validator reads through TypeDescriptor is wrapped (see the first test).
        /// </summary>
        public class CodeThatValidatorRuns
        {
            [Fact]
            public void Getter_that_an_attribute_makes_Validator_read()
            {
                // Validator reads the property to give its value to [Required]. It does that with
                // TypeDescriptor, not with the reflection call the walk uses.
                // See: https://learn.microsoft.com/dotnet/api/system.componentmodel.typedescriptor.getproperties
                var thrown = Record.Exception(() => Run(new AttributeOnBrokenGetter(), out _));

                Assert.NotNull(thrown);
                Assert.Equal(typeof(TargetInvocationException), thrown.GetType());
                Assert.IsType<InvalidOperationException>(thrown.InnerException);
            }

            [Fact]
            public void Validate_method_that_throws()
            {
                var thrown = Assert.Throws<InvalidOperationException>(() => Run(new SelfValidatingThatThrows(), out _));

                Assert.Equal("validate failed", thrown.Message);
            }

            [Fact]
            public void Validate_method_that_throws_on_a_collection_item()
            {
                var thrown = Assert.Throws<InvalidOperationException>(() => Run(new HoldsSelfValidatingThatThrows(), out _));

                Assert.Equal("validate failed", thrown.Message);
            }

            [Fact]
            public void Attribute_that_throws()
            {
                var thrown = Assert.Throws<InvalidOperationException>(() => Run(new AttributeThatThrows { Name = "x" }, out _));

                Assert.Equal("attribute failed", thrown.Message);
            }

            [Fact]
            public async Task Attribute_that_throws_in_the_async_method()
            {
                var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new RecursiveDataAnnotationValidator().TryValidateObjectRecursiveAsync(
                        new AttributeThatThrows { Name = "x" }, new List<ValidationResult>()));

                Assert.Equal("attribute failed", thrown.Message);
            }
        }
    }
}
