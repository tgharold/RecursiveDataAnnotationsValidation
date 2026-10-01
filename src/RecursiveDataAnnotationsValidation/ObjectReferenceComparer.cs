using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RecursiveDataAnnotationsValidation
{
    /// <summary>
    /// Compares objects by reference, ignoring any Equals or GetHashCode override.
    /// netstandard2.0 has no ReferenceEqualityComparer, so the library has its own.
    /// </summary>
    internal sealed class ObjectReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ObjectReferenceComparer Instance = new ObjectReferenceComparer();

        private ObjectReferenceComparer()
        {
        }

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
