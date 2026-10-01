using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using RecursiveDataAnnotationsValidation.Attributes;

namespace RecursiveDataAnnotationsValidation.Extensions
{
    internal static class TypeExtensions
    {
        private static readonly ConcurrentDictionary<Type, Type[]> ElementTypesCache =
            new ConcurrentDictionary<Type, Type[]>();

        // Attributes can be added at runtime with TypeDescriptor.AddAttributes, which raises
        // TypeDescriptor.Refreshed. Each cached answer carries the version it was computed under,
        // so an answer computed while a refresh happened is recomputed on the next lookup.
        private static readonly ConcurrentDictionary<Type, (int Version, bool IsLeaf)> LeafTypeCache =
            new ConcurrentDictionary<Type, (int Version, bool IsLeaf)>();

        private static int _typeDescriptorVersion;

        static TypeExtensions()
        {
            TypeDescriptor.Refreshed += _ => System.Threading.Interlocked.Increment(ref _typeDescriptorVersion);
        }

        /// <summary>
        /// True when every element type the collection declares is a leaf type (see
        /// <see cref="IsLeafType"/>). The element types come from the array element type, or from
        /// each IEnumerable&lt;T&gt; the collection implements. Decided from the type alone, so
        /// nothing is enumerated.
        /// </summary>
        public static bool IsCollectionOfLeafType(this Type collectionType)
        {
            var elementTypes = ElementTypesCache.GetOrAdd(collectionType, FindElementTypes);

            return elementTypes.Length > 0 && elementTypes.All(IsLeafType);
        }

        /// <summary>
        /// True when validating an item of this type can never produce a result. A Nullable&lt;T&gt;
        /// is checked as T. The type must be a value type or a sealed class, so an item cannot be
        /// a derived type with its own attributes. It must also pass all four checks:
        /// 1. No validation attribute on the type.
        /// 2. No validation attribute on any of its properties.
        /// 3. It does not implement IValidatableObject.
        /// 4. No property the validator walks into (see <see cref="IsWalked"/>).
        /// Checks 1 and 2 use TypeDescriptor, like Validator, so attributes added at runtime count.
        /// </summary>
        public static bool IsLeafType(this Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            var version = System.Threading.Volatile.Read(ref _typeDescriptorVersion);
            if (LeafTypeCache.TryGetValue(type, out var cached) && cached.Version == version)
                return cached.IsLeaf;

            var isLeaf = FindIsLeafType(type);
            LeafTypeCache[type] = (version, isLeaf);
            return isLeaf;
        }

        /// <summary>
        /// True for a property the validator walks into: readable, not an indexer, not marked
        /// [SkipRecursiveValidation], and of a reference type other than string.
        /// </summary>
        public static bool IsWalked(this PropertyInfo property)
        {
            return property.PropertyType != typeof(string)
                && !property.PropertyType.IsValueType
                && property.CanRead
                && property.GetIndexParameters().Length == 0
                && !property.IsDefined(typeof(SkipRecursiveValidationAttribute), false);
        }

        private static Type[] FindElementTypes(Type collectionType)
        {
            if (collectionType.IsArray) return new[] { collectionType.GetElementType() };

            return collectionType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .ToArray();
        }

        private static bool FindIsLeafType(Type type)
        {
            return (type.IsValueType || type.IsSealed)
                && !HasValidationAttributes(type)
                && !typeof(IValidatableObject).IsAssignableFrom(type)
                && !type.GetProperties().Any(IsWalked);
        }

        /// <summary>
        /// Looks for validation attributes the way Validator does: through TypeDescriptor, on the
        /// type and on its properties. TypeDescriptor includes attributes added at runtime.
        /// </summary>
        private static bool HasValidationAttributes(Type type)
        {
            return TypeDescriptor.GetAttributes(type).OfType<ValidationAttribute>().Any()
                || TypeDescriptor.GetProperties(type).Cast<PropertyDescriptor>()
                    .Any(p => p.Attributes.OfType<ValidationAttribute>().Any());
        }
    }
}
