using System;
using System.Collections.Generic;
using System.Linq;

namespace RecursiveDataAnnotationsValidation.Extensions
{
    internal static class TypeExtensions
    {
        /// <summary>
        /// True when every element type the collection can yield is a leaf type (see
        /// <see cref="IsLeafType"/>). Decided from the type alone, so nothing is enumerated.
        /// </summary>
        public static bool IsCollectionOfLeafType(this Type collectionType)
        {
            if (collectionType.IsArray) return collectionType.GetElementType().IsLeafType();

            var elementTypes = collectionType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .ToList();

            return elementTypes.Count > 0 && elementTypes.All(IsLeafType);
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
    }
}
