using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using RecursiveDataAnnotationsValidation.Extensions;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests.Extensions
{
    /// <summary>
    /// A leaf type is one where validating an item of that type can never produce a result.
    /// The validator skips a collection whose element types are all leaf types.
    /// An item can produce a result in only four ways, so a leaf type must pass all four checks:
    /// 1. No validation attribute on the type.
    /// 2. No validation attribute on any of its properties.
    /// 3. It does not implement IValidatableObject. Validator calls Validate() on any item
    ///    that implements it.
    /// 4. No property the validator would walk into: a readable, non-indexer property of a
    ///    reference type other than string.
    /// Checks 1 and 2 use TypeDescriptor, like Validator, so attributes added at runtime count.
    /// The type must also be a value type or a sealed class. Otherwise a collection declared
    /// as List&lt;Base&gt; could hold a derived object that has its own attributes.
    /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.ivalidatableobject
    /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/sealed
    /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/value-types
    /// The library targets netstandard2.0, so it cannot name newer types such as DateOnly or
    /// Int128. The tests run on net8.0, where those types exist, and show that the checks find
    /// them by their shape instead of by name.
    /// See: https://learn.microsoft.com/dotnet/standard/frameworks
    /// </summary>
    public class TypeExtensionsTests
    {
        public class Child
        {
        }

        public struct PlainPoint
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        public sealed class SealedTag
        {
            public string Name { get; set; }
            public int Weight { get; set; }
        }

        // Fails check 3.
        public struct SelfValidatingPoint : IValidatableObject
        {
            public int X { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
                Array.Empty<ValidationResult>();
        }

        // Fails check 4: the validator walks into Child.
        public struct PointWithChild
        {
            public Child Owner { get; set; }
        }

        // Not sealed, so a List<UnsealedTag> could hold a derived object with attributes.
        public class UnsealedTag
        {
            public string Name { get; set; }
        }

        [AttributeUsage(AttributeTargets.Enum)]
        public class AlwaysInvalidAttribute : ValidationAttribute
        {
            public override bool IsValid(object value) => false;
        }

        [AlwaysInvalid]
        public enum CheckedColor { Red }

        /// <summary>Yields both ints and objects, so it must not be skipped.</summary>
        public class MixedSequence : IEnumerable<int>, IEnumerable<Child>
        {
            IEnumerator<int> IEnumerable<int>.GetEnumerator() => throw new NotSupportedException();
            IEnumerator<Child> IEnumerable<Child>.GetEnumerator() => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
        }

        [Theory]
        [InlineData(typeof(byte[]))]
        [InlineData(typeof(int[,]))]
        [InlineData(typeof(int?[]))]
        [InlineData(typeof(List<int>))]
        [InlineData(typeof(HashSet<Guid>))]
        [InlineData(typeof(List<DayOfWeek>))]
        [InlineData(typeof(string))]
        [InlineData(typeof(string[]))]
        [InlineData(typeof(Dictionary<string, int>))]
        [InlineData(typeof(Dictionary<string, string>))]
        [InlineData(typeof(DateOnly[]))]
        [InlineData(typeof(List<TimeOnly>))]
        [InlineData(typeof(Int128[]))]
        [InlineData(typeof(Half[]))]
        [InlineData(typeof(List<System.Numerics.BigInteger>))]
        [InlineData(typeof(List<PlainPoint>))]
        [InlineData(typeof(List<PlainPoint?>))]
        [InlineData(typeof(List<SealedTag>))]
        public void Collection_of_leaf_type_is_detected(Type type)
        {
            Assert.True(type.IsCollectionOfLeafType());
        }

        [Theory]
        [InlineData(typeof(Child[]))]
        [InlineData(typeof(object[]))]
        [InlineData(typeof(int[][]))]
        [InlineData(typeof(List<Child>))]
        [InlineData(typeof(Dictionary<string, Child>))]
        [InlineData(typeof(Dictionary<Child, int>))]
        [InlineData(typeof(CheckedColor[]))]
        [InlineData(typeof(List<CheckedColor?>))]
        [InlineData(typeof(Dictionary<string, CheckedColor>))]
        [InlineData(typeof(ArrayList))]
        [InlineData(typeof(MixedSequence))]
        [InlineData(typeof(List<SelfValidatingPoint>))]
        [InlineData(typeof(List<PointWithChild>))]
        [InlineData(typeof(List<UnsealedTag>))]
        public void Collection_that_can_yield_other_types_is_not_detected(Type type)
        {
            Assert.False(type.IsCollectionOfLeafType());
        }
    }
}
