using System;
using System.Collections;
using System.Collections.Generic;
using RecursiveDataAnnotationsValidation.Extensions;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests.Extensions
{
    public class TypeExtensionsTests
    {
        public class Child
        {
        }

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
        public void Collection_of_leaf_type_is_detected(Type type)
        {
            Assert.True(type.IsCollectionOfLeafType());
        }

        [Theory]
        [InlineData(typeof(Child[]))]
        [InlineData(typeof(object[]))]
        [InlineData(typeof(int[][]))]
        [InlineData(typeof(List<Child>))]
        [InlineData(typeof(Dictionary<string, int>))]
        [InlineData(typeof(ArrayList))]
        [InlineData(typeof(MixedSequence))]
        public void Collection_that_can_yield_other_types_is_not_detected(Type type)
        {
            Assert.False(type.IsCollectionOfLeafType());
        }
    }
}
