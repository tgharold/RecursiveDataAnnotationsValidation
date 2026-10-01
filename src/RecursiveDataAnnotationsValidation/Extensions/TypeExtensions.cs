using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RecursiveDataAnnotationsValidation.Extensions
{
    internal static class TypeExtensions
    {
        private static readonly ConcurrentDictionary<Type, bool> CollectionOfLeafTypeCache =
            new ConcurrentDictionary<Type, bool>();

        /// <summary>
        /// True when every element type the collection declares is a leaf type (see
        /// <see cref="IsLeafType"/>), or a KeyValuePair of two leaf types. The element types come
        /// from the array element type, or from each IEnumerable&lt;T&gt; the collection implements.
        /// Decided from the type alone, so nothing is enumerated.
        /// </summary>
        public static bool IsCollectionOfLeafType(this Type collectionType)
        {
            return CollectionOfLeafTypeCache.GetOrAdd(collectionType, FindIsCollectionOfLeafType);
        }

        /// <summary>
        /// True for types that carry no DataAnnotations to validate: primitives, enums, string,
        /// decimal, DateTime, DateTimeOffset, TimeSpan and Guid, and Nullable of any of those.
        /// </summary>
        public static bool IsLeafType(this Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }

        private static bool FindIsCollectionOfLeafType(Type collectionType)
        {
            if (collectionType.IsArray) return IsLeafElementType(collectionType.GetElementType());

            var elementTypes = collectionType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .ToList();

            return elementTypes.Count > 0 && elementTypes.All(IsLeafElementType);
        }

        private static bool IsLeafElementType(Type elementType)
        {
            if (elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                return elementType.GetGenericArguments().All(IsLeafType);

            return elementType.IsLeafType();
        }
    }
}
