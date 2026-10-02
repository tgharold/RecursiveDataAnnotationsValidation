using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
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

        private static readonly ConcurrentDictionary<Type, bool> OverridesEqualsCache =
            new ConcurrentDictionary<Type, bool>();

        // A boxed default(T) for each struct type. GetUninitializedObject returns zeroed memory, so
        // it is default(T) even for a struct that declares a parameterless constructor, which
        // Activator.CreateInstance would run.
        private static readonly ConcurrentDictionary<Type, object> DefaultValues =
            new ConcurrentDictionary<Type, object>();

        private static readonly ConcurrentDictionary<Type, bool> UnsafeToWalkCache =
            new ConcurrentDictionary<Type, bool>();

        // Framework types whose properties throw, or never end, when the validator walks them.
        // They carry no validation attributes. Only the properties these types and their framework
        // subclasses declare are skipped, so a user subclass still has its own properties walked.
        private static readonly Type[] UnsafeToWalkTypes =
        {
            typeof(MemberInfo),     // Type, MethodInfo: DeclaringMethod and others throw
            typeof(Assembly),
            typeof(Module),
            typeof(Delegate),       // Method is a MethodInfo, and Target is a closure object
            typeof(Uri),            // a relative Uri throws from Segments and others
            typeof(FileSystemInfo), // DirectoryInfo.Root returns a new DirectoryInfo on each read
            typeof(Thread),         // CurrentCulture and others throw when read from another thread
            typeof(Process),        // StartInfo and others throw for a process this object did not start
        };

        // The types IsLeafType is deciding on this thread. A type that yields itself, such as
        // `sealed class Node : IEnumerable<Node>`, asks about its own type while it is being decided.
        [ThreadStatic]
        private static HashSet<Type> _typesBeingChecked;

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
        /// a derived type with its own attributes. It must also pass all five checks:
        /// 1. No validation attribute on the type.
        /// 2. No validation attribute on any of its properties.
        /// 3. It does not implement IValidatableObject.
        /// 4. No property the validator walks into (see <see cref="IsWalked"/>).
        /// 5. If it is a collection, it is a collection of leaf types (see <see cref="IsCollectionOfLeafType"/>),
        ///    because the validator enumerates an item that is a collection, and the items it
        ///    yields can have attributes of their own. A collection of an unknown item type, such as
        ///    a non-generic one, is not a leaf type.
        /// Checks 1 and 2 use TypeDescriptor, like Validator, so attributes added at runtime count.
        /// </summary>
        public static bool IsLeafType(this Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            var version = System.Threading.Volatile.Read(ref _typeDescriptorVersion);
            if (LeafTypeCache.TryGetValue(type, out var cached) && cached.Version == version)
                return cached.IsLeaf;

            var typesBeingChecked = _typesBeingChecked ?? (_typesBeingChecked = new HashSet<Type>());

            // A type that is already being decided is not a leaf type. That is the safe answer,
            // because the validator then looks at the type instead of skipping it.
            if (!typesBeingChecked.Add(type)) return false;

            bool isLeaf;
            try
            {
                isLeaf = FindIsLeafType(type);
            }
            finally
            {
                typesBeingChecked.Remove(type);
            }

            LeafTypeCache[type] = (version, isLeaf);
            return isLeaf;
        }

        /// <summary>
        /// True for a property the validator walks into: readable, not an indexer, not marked
        /// [SkipRecursiveValidation], of a reference type other than string or of a struct that is a
        /// collection of items that can have attributes, and not declared by a framework type whose
        /// properties are unsafe to read (see <see cref="IsUnsafeToWalk"/>).
        /// </summary>
        public static bool IsWalked(this PropertyInfo property)
        {
            return property.PropertyType != typeof(string)
                && IsWalkedType(property.PropertyType)
                && property.CanRead
                && property.GetIndexParameters().Length == 0
                && !property.IsDefined(typeof(SkipRecursiveValidationAttribute), false)
                && !property.DeclaringType.IsUnsafeToWalk();
        }

        /// <summary>
        /// The public instance properties of the type that the validator walks (see
        /// <see cref="IsWalked"/>). A static property is left out: it holds data of the type, not
        /// of the object, and Validator ignores it too. Type.GetProperties() with no arguments
        /// would return public static properties as well.
        /// </summary>
        public static List<PropertyInfo> GetWalkedProperties(this Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(IsWalked).ToList();
        }

        /// <summary>
        /// True when the type's Equals(object) is not object.Equals, so two different objects
        /// of the type can be equal. Records, structs (through ValueType.Equals) and classes
        /// that override Equals all count.
        /// </summary>
        public static bool OverridesEquals(this Type type)
        {
            return OverridesEqualsCache.GetOrAdd(type, t =>
                t.GetMethod("Equals", new[] { typeof(object) }).DeclaringType != typeof(object));
        }

        /// <summary>
        /// True for a framework type whose own properties the validator does not walk, because
        /// reading them throws or never ends, such as Uri.Segments on a relative Uri. Framework
        /// types derived from one count too, such as RuntimeType, the type of typeof(...).
        /// A type outside the System namespaces, such as a user's subclass of Uri, does not count,
        /// so the properties it adds are walked. See <see cref="UnsafeToWalkTypes"/>.
        /// </summary>
        public static bool IsUnsafeToWalk(this Type type)
        {
            return UnsafeToWalkCache.GetOrAdd(type, t =>
                IsInSystemNamespace(t)
                && UnsafeToWalkTypes.Any(unsafeType => unsafeType.IsAssignableFrom(t)));
        }

        /// <summary>
        /// True for a struct that equals its default value, such as an ImmutableArray or an
        /// ArraySegment that nobody set, or a struct of your own whose fields are all null or zero.
        /// A default struct collection usually holds nothing to validate, and enumerating one throws:
        /// InvalidOperationException for these two framework types, NullReferenceException for a
        /// struct that wraps an array. The validator skips it, so a model that has an unset struct
        /// field does not start to throw.
        /// The struct is compared with default(T) by its memory. The Equals of the caller's type is
        /// not called: it can say "equal" for a struct that holds objects, such as one that compares
        /// only an Id, which would let invalid objects pass, and it can throw. A struct with no
        /// fields is always default.
        /// See: https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.runtimehelpers.equals
        /// </summary>
        public static bool IsDefaultStruct(this object obj)
        {
            var type = obj.GetType();

            return type.IsValueType
                && RuntimeHelpers.Equals(obj, DefaultValues.GetOrAdd(type, FormatterServices.GetUninitializedObject));
        }

        // A property of a reference type is walked. A property of a struct is not, because a struct such
        // as a Point or a Money has nothing to walk into. The exception is a struct that is a collection
        // of items that can have attributes, such as ImmutableArray<T> of a class, because the same
        // struct is enumerated when it is an item of another collection, and a model must not pass
        // because the collection sits one level higher. A Nullable<T> is checked as T.
        private static bool IsWalkedType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return !type.IsValueType
                || (typeof(IEnumerable).IsAssignableFrom(type) && !type.IsCollectionOfLeafType());
        }

        private static bool IsInSystemNamespace(Type type)
        {
            var ns = type.Namespace;
            return ns != null && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal));
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
                && type.GetWalkedProperties().Count == 0
                && (!typeof(IEnumerable).IsAssignableFrom(type) || type.IsCollectionOfLeafType());
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
