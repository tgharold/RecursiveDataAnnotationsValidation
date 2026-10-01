using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace RecursiveDataAnnotationsValidation.Extensions
{
    internal static class TypeExtensions
    {
        private static readonly ConcurrentDictionary<Type, Type[]> ElementTypesCache =
            new ConcurrentDictionary<Type, Type[]>();

        // Attributes can be added at runtime with TypeDescriptor.AddAttributes, which raises
        // TypeDescriptor.Refreshed. Each cached answer carries the version it was computed under,
        // so an answer computed while a refresh happened is recomputed on the next lookup.
        private static readonly ConcurrentDictionary<Type, (int Version, bool HasAttributes)> ValidationAttributesCache =
            new ConcurrentDictionary<Type, (int Version, bool HasAttributes)>();

        private static int _typeDescriptorVersion;

        static TypeExtensions()
        {
            TypeDescriptor.Refreshed += _ => System.Threading.Interlocked.Increment(ref _typeDescriptorVersion);
        }

        /// <summary>
        /// True when every element type the collection declares is a leaf type (see
        /// <see cref="IsLeafType"/>), or a KeyValuePair of two leaf types. The element types come
        /// from the array element type, or from each IEnumerable&lt;T&gt; the collection implements.
        /// Decided from the type alone, so nothing is enumerated.
        /// </summary>
        public static bool IsCollectionOfLeafType(this Type collectionType)
        {
            var elementTypes = ElementTypesCache.GetOrAdd(collectionType, FindElementTypes);

            return elementTypes.Length > 0 && elementTypes.All(IsLeafElementType);
        }

        /// <summary>
        /// True for types with nothing to validate: primitives, enums, string, decimal, DateTime,
        /// DateTimeOffset, TimeSpan and Guid, and Nullable of any of those, as long as no
        /// validation attribute is attached to the type, in source or at runtime.
        /// </summary>
        public static bool IsLeafType(this Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            var isLeafKind = type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);

            return isLeafKind && !HasValidationAttributes(type);
        }

        private static Type[] FindElementTypes(Type collectionType)
        {
            if (collectionType.IsArray) return new[] { collectionType.GetElementType() };

            return collectionType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .ToArray();
        }

        private static bool IsLeafElementType(Type elementType)
        {
            if (elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                return !HasValidationAttributes(elementType)
                    && elementType.GetGenericArguments().All(IsLeafType);

            return elementType.IsLeafType();
        }

        /// <summary>
        /// Looks for validation attributes the way Validator does: through TypeDescriptor, on the
        /// type and on its properties. TypeDescriptor includes attributes added at runtime.
        /// </summary>
        private static bool HasValidationAttributes(Type type)
        {
            var version = System.Threading.Volatile.Read(ref _typeDescriptorVersion);
            if (ValidationAttributesCache.TryGetValue(type, out var cached) && cached.Version == version)
                return cached.HasAttributes;

            var hasAttributes = FindValidationAttributes(type);
            ValidationAttributesCache[type] = (version, hasAttributes);
            return hasAttributes;
        }

        private static bool FindValidationAttributes(Type type)
        {
            return TypeDescriptor.GetAttributes(type).OfType<ValidationAttribute>().Any()
                || TypeDescriptor.GetProperties(type).Cast<PropertyDescriptor>()
                    .Any(p => p.Attributes.OfType<ValidationAttribute>().Any());
        }
    }
}
