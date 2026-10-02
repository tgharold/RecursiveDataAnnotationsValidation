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
    /// An item can produce a result in only five ways, so a leaf type must pass all five checks:
    /// 1. No validation attribute on the type.
    /// 2. No validation attribute on any of its properties.
    /// 3. It does not implement IValidatableObject. Validator calls Validate() on any item
    ///    that implements it.
    /// 4. No property the validator would walk into: a readable, non-indexer property of a
    ///    reference type other than string, or of a struct that is not a leaf type itself.
    /// 5. If it is a collection, it is a collection of leaf types. The validator enumerates an
    ///    item that is a collection, so a sealed class or a struct that yields objects must not
    ///    be skipped. A collection that yields an unknown type, such as a non-generic one, counts
    ///    as one that yields objects.
    /// Checks 1 and 2 use TypeDescriptor, like Validator, so attributes added at runtime count.
    /// The type must also be a value type or a sealed class. Otherwise a collection declared
    /// as List&lt;Base&gt; could hold a derived object that has its own attributes.
    /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.ivalidatableobject
    /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/sealed
    /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/value-types
    /// The library targets netstandard2.0, so it cannot name newer types such as DateOnly or
    /// Int128. The tests on net8.0 and later, where those types exist, show that the checks find
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

        // Collections of leaf types and of objects, sealed or a struct, for check 5.
        public sealed class SealedIntBag : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
        }

        public sealed class SealedChildBag : IEnumerable<Child>
        {
            public IEnumerator<Child> GetEnumerator() => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
        }

        public struct ChildBagStruct : IEnumerable<Child>
        {
            public IEnumerator<Child> GetEnumerator() => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
        }

        public sealed class NonGenericBag : IEnumerable
        {
            public IEnumerator GetEnumerator() => throw new NotSupportedException();
        }

        // Yields its own type. Deciding that it is a leaf type asks whether it is a leaf type, and
        // without a guard that would never end. It is not a leaf type, because it yields objects.
        public sealed class SelfYieldingBag : IEnumerable<SelfYieldingBag>
        {
            public IEnumerator<SelfYieldingBag> GetEnumerator() => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
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
#if NET8_0_OR_GREATER
        // These types do not exist on .NET Framework, so the net481 build leaves them out.
        // See: https://learn.microsoft.com/dotnet/csharp/language-reference/preprocessor-directives
        [InlineData(typeof(DateOnly[]))]
        [InlineData(typeof(List<TimeOnly>))]
        [InlineData(typeof(Int128[]))]
        [InlineData(typeof(Half[]))]
#endif
        [InlineData(typeof(List<System.Numerics.BigInteger>))]
        [InlineData(typeof(List<PlainPoint>))]
        [InlineData(typeof(List<PlainPoint?>))]
        [InlineData(typeof(List<SealedTag>))]
        [InlineData(typeof(List<SealedIntBag>))]
#if NET8_0_OR_GREATER
        [InlineData(typeof(List<System.Collections.Immutable.ImmutableArray<int>>))]
        [InlineData(typeof(List<System.Collections.Immutable.ImmutableList<Guid>>))]
#endif
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
        [InlineData(typeof(List<SealedChildBag>))]
        [InlineData(typeof(List<ChildBagStruct>))]
        [InlineData(typeof(List<NonGenericBag>))]
        [InlineData(typeof(List<SelfYieldingBag>))]
        [InlineData(typeof(SelfYieldingBag[]))]
#if NET8_0_OR_GREATER
        [InlineData(typeof(List<System.Collections.Immutable.ImmutableArray<Child>>))]
        [InlineData(typeof(List<System.Collections.Immutable.ImmutableList<Child>>))]
#endif
        public void Collection_that_can_yield_other_types_is_not_detected(Type type)
        {
            Assert.False(type.IsCollectionOfLeafType());
        }

        [Fact]
        public void Sealed_collection_of_objects_is_not_a_leaf_type()
        {
            Assert.False(typeof(SealedChildBag).IsLeafType());
            Assert.False(typeof(ChildBagStruct).IsLeafType());
            Assert.False(typeof(NonGenericBag).IsLeafType());
            Assert.False(typeof(SelfYieldingBag).IsLeafType());
        }

        [Fact]
        public void Sealed_collection_of_values_is_a_leaf_type()
        {
            Assert.True(typeof(SealedIntBag).IsLeafType());
            Assert.True(typeof(string).IsLeafType());
        }

        public class StructBagHolder
        {
            public ChildBagStruct Bag { get; set; }

            public ChildBagStruct? MaybeBag { get; set; }

            public PlainPoint Point { get; set; }

            public KeyValuePair<string, Child> Pair { get; set; }

            public PointWithChild WithChild { get; set; }

            public SelfValidatingPoint SelfValidating { get; set; }

            public SelfValidatingPoint? MaybeSelfValidating { get; set; }

            public DateTime When { get; set; }

            public DateTime? MaybeWhen { get; set; }
        }

        // A property of a struct is walked when the struct is not a leaf type: it is a collection of
        // items that can have attributes, or it has something to validate itself, such as an
        // IValidatableObject or a property that the validator walks. A KeyValuePair is not an
        // IEnumerable, but its Value can hold an object.
        [Fact]
        public void Struct_collection_property_is_walked()
        {
            Assert.True(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.Bag)).IsWalked());
            Assert.True(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.MaybeBag)).IsWalked());
        }

        [Fact]
        public void Struct_property_with_something_to_validate_is_walked()
        {
            Assert.True(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.Pair)).IsWalked());
            Assert.True(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.WithChild)).IsWalked());
            Assert.True(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.SelfValidating)).IsWalked());
            Assert.True(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.MaybeSelfValidating)).IsWalked());
        }

        [Fact]
        public void Struct_property_of_a_leaf_type_is_not_walked()
        {
            Assert.False(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.Point)).IsWalked());
            Assert.False(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.When)).IsWalked());
            Assert.False(typeof(StructBagHolder).GetProperty(nameof(StructBagHolder.MaybeWhen)).IsWalked());
        }

        // DateTime.Date returns a DateTime. A property that returns its own struct type is not walked,
        // so DateTime has no walked property and stays a leaf type. Without that rule, deciding
        // whether DateTime is a leaf type would ask about DateTime again, and the guard against
        // that answers "not a leaf type". Every DateTime property would then be walked.
        // See: https://learn.microsoft.com/dotnet/api/system.datetime.date
        [Fact]
        public void Struct_property_of_its_own_type_is_not_walked()
        {
            Assert.False(typeof(DateTime).GetProperty(nameof(DateTime.Date)).IsWalked());
            Assert.True(typeof(DateTime).IsLeafType());
            Assert.True(typeof(DateTimeOffset).IsLeafType());
            Assert.True(typeof(TimeSpan).IsLeafType());
        }

        // Check 4 of IsLeafType uses IsWalked. A type whose only walked member is a struct collection
        // was a leaf type before struct collection properties were walked, and it is not now.
        [Fact]
        public void Type_with_a_struct_collection_property_is_not_a_leaf_type()
        {
            Assert.False(typeof(SealedStructBagHolder).IsLeafType());
        }

        public sealed class SealedStructBagHolder
        {
            public ChildBagStruct Bag { get; set; }
        }

#if NET8_0_OR_GREATER
        public class ImmutableArrayHolder
        {
            public System.Collections.Immutable.ImmutableArray<Child> Children { get; set; }

            public System.Collections.Immutable.ImmutableArray<Child>? MaybeChildren { get; set; }

            public System.Collections.Immutable.ImmutableArray<int> Numbers { get; set; }
        }

        [Fact]
        public void Immutable_array_property_of_objects_is_walked()
        {
            Assert.True(typeof(ImmutableArrayHolder).GetProperty(nameof(ImmutableArrayHolder.Children)).IsWalked());
            Assert.True(typeof(ImmutableArrayHolder).GetProperty(nameof(ImmutableArrayHolder.MaybeChildren)).IsWalked());
        }

        // A collection of leaf types has nothing to validate, so its property is not walked.
        [Fact]
        public void Immutable_array_property_of_values_is_not_walked()
        {
            Assert.False(typeof(ImmutableArrayHolder).GetProperty(nameof(ImmutableArrayHolder.Numbers)).IsWalked());
        }
#endif

        // A default struct collection holds nothing, and enumerating one throws, so the validator skips it.
        // The check compares the memory of the struct with default(T), so it needs no list of types.
        [Fact]
        public void Default_struct_is_detected()
        {
            Assert.True(((object)default(ArraySegment<Child>)).IsDefaultStruct());
            Assert.True(((object)default(PlainPoint)).IsDefaultStruct());
            Assert.True(((object)default(int)).IsDefaultStruct());
        }

#if NET8_0_OR_GREATER
        // System.Collections.Immutable is not part of net481.
        [Fact]
        public void Default_immutable_array_is_detected()
        {
            Assert.True(((object)default(System.Collections.Immutable.ImmutableArray<Child>)).IsDefaultStruct());
        }

        [Fact]
        public void Immutable_array_that_has_a_value_is_not_detected()
        {
            Assert.False(((object)System.Collections.Immutable.ImmutableArray.Create(new Child())).IsDefaultStruct());
            Assert.False(((object)System.Collections.Immutable.ImmutableArray<Child>.Empty).IsDefaultStruct());
        }
#endif

        [Fact]
        public void Struct_that_has_a_value_is_not_detected()
        {
            Assert.False(((object)new ArraySegment<Child>(new[] { new Child() })).IsDefaultStruct());
            Assert.False(((object)new ArraySegment<Child>(new Child[0])).IsDefaultStruct());
            Assert.False(((object)new PlainPoint { X = 1 }).IsDefaultStruct());
            Assert.False(((object)5).IsDefaultStruct());
        }

        // The check compares the memory of the struct with default(T). It does not call the Equals of
        // the caller's type, which can say "equal" for a struct that holds objects, or throw.
        [Fact]
        public void Struct_whose_Equals_ignores_its_items_is_not_a_default_struct()
        {
            var bag = new StructCollectionPropertyTests.StructsWithOddEquals.IdBag(0, new StructCollectionPropertyTests.Leaf());

            Assert.True(bag.Equals(default(StructCollectionPropertyTests.StructsWithOddEquals.IdBag)));
            Assert.False(((object)bag).IsDefaultStruct());
        }

        [Fact]
        public void Struct_whose_Equals_throws_is_checked_without_calling_it()
        {
            Assert.True(((object)default(StructCollectionPropertyTests.StructsWithOddEquals.SequenceBag)).IsDefaultStruct());
            Assert.False(((object)new StructCollectionPropertyTests.StructsWithOddEquals.SequenceBag(new StructCollectionPropertyTests.Leaf())).IsDefaultStruct());
        }

        [Fact]
        public void Object_of_a_class_is_not_a_default_struct()
        {
            Assert.False(new List<Child>().IsDefaultStruct());
            Assert.False("text".IsDefaultStruct());
            Assert.False(new object().IsDefaultStruct());
        }

        // Framework types whose properties throw, block or never end when walked, and types derived
        // from them. A null SqlString throws SqlNullValueException from CompareInfo, a GCHandle that
        // is not allocated throws from Target, and ValueTask<T>.Result waits for a task that has not
        // finished. Each SqlTypes type implements INullable, so INullable covers all of them.
        // See: https://learn.microsoft.com/dotnet/api/system.data.sqltypes.inullable
        // See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.valuetask-1.result
        [Theory]
        [InlineData(typeof(Type))]
        [InlineData(typeof(System.Reflection.MethodInfo))]
        [InlineData(typeof(System.Reflection.Assembly))]
        [InlineData(typeof(System.Reflection.Module))]
        [InlineData(typeof(Action))]
        [InlineData(typeof(Func<int>))]
        [InlineData(typeof(Uri))]
        [InlineData(typeof(System.IO.DirectoryInfo))]
        [InlineData(typeof(System.IO.FileInfo))]
        [InlineData(typeof(System.Runtime.InteropServices.GCHandle))]
        [InlineData(typeof(System.Data.SqlTypes.SqlString))]
        [InlineData(typeof(System.Data.SqlTypes.SqlDecimal))]
        [InlineData(typeof(System.Data.SqlTypes.SqlBytes))]
#if NET8_0_OR_GREATER
        // ValueTask<T> is not part of .NET Framework 4.8.1 without a package.
        [InlineData(typeof(System.Threading.Tasks.ValueTask<Child>))]
#endif
        public void Unsafe_to_walk_type_is_detected(Type type)
        {
            Assert.True(type.IsUnsafeToWalk());
        }

        // A denied struct has no property left to walk, so it is a leaf type, and a property of
        // that type is skipped.
        [Fact]
        public void Denied_struct_is_a_leaf_type()
        {
            Assert.True(typeof(System.Runtime.InteropServices.GCHandle).IsLeafType());
            Assert.True(typeof(System.Data.SqlTypes.SqlString).IsLeafType());
#if NET8_0_OR_GREATER
            Assert.True(typeof(System.Threading.Tasks.ValueTask<Child>).IsLeafType());
#endif
        }

        // INullable is denied only for framework types. A type of your own that implements it is
        // walked like any other.
        public class UserNullable : System.Data.SqlTypes.INullable
        {
            public bool IsNull => false;

            public Child Owner { get; set; }
        }

        [Fact]
        public void User_type_that_implements_INullable_is_not_detected()
        {
            Assert.False(typeof(UserNullable).IsUnsafeToWalk());
            Assert.True(typeof(UserNullable).GetProperty(nameof(UserNullable.Owner)).IsWalked());
        }

        [Fact]
        public void Runtime_type_object_is_unsafe_to_walk()
        {
            // typeof(...) returns a RuntimeType, which derives from Type.
            Assert.True(typeof(string).GetType().IsUnsafeToWalk());
        }

        // Framework types that hold user objects, or that do not throw, are still walked.
        [Theory]
        [InlineData(typeof(object))]
        [InlineData(typeof(Child))]
        [InlineData(typeof(Tuple<Child, int>))]
        [InlineData(typeof(KeyValuePair<string, Child>))]
        [InlineData(typeof(List<Child>))]
        [InlineData(typeof(System.IO.Stream))]
        [InlineData(typeof(Exception))]
        [InlineData(typeof(System.Threading.Tasks.Task<Child>))]
        [InlineData(typeof(System.Threading.CancellationToken))]
        public void Walkable_type_is_not_detected(Type type)
        {
            Assert.False(type.IsUnsafeToWalk());
        }

        public class UserUri : Uri
        {
            public UserUri(string uriString) : base(uriString)
            {
            }

            public Child Owner { get; set; }
        }

        // A user's subclass is outside the System namespaces, so the properties it adds are walked.
        // The properties it inherits are declared by Uri, so they are not.
        [Fact]
        public void User_subclass_of_a_denied_type_is_not_detected()
        {
            Assert.False(typeof(UserUri).IsUnsafeToWalk());
        }

        // The namespace check has three outcomes besides "System": no namespace, a namespace that
        // only begins with the letters "System", and a nested System.* namespace.
        // See: https://learn.microsoft.com/dotnet/api/system.type.namespace
        [Fact]
        public void Subclass_in_the_global_namespace_is_not_detected()
        {
            // Type.Namespace is null for a type declared outside any namespace.
            Assert.Null(typeof(GlobalNamespaceUri).Namespace);

            Assert.False(typeof(GlobalNamespaceUri).IsUnsafeToWalk());
            Assert.True(typeof(GlobalNamespaceUri).GetProperty(nameof(GlobalNamespaceUri.Owner)).IsWalked());
            Assert.False(typeof(GlobalNamespaceUri).GetProperty(nameof(Uri.Segments)).IsWalked());
        }

        [Theory]
        [InlineData(typeof(SystemLookalike.LookalikeUri), "SystemLookalike")]
        [InlineData(typeof(Systematic.SystematicUri), "Systematic")]
        public void Subclass_in_a_namespace_that_only_begins_with_System_is_not_detected(Type type, string expectedNamespace)
        {
            Assert.Equal(expectedNamespace, type.Namespace);

            Assert.False(type.IsUnsafeToWalk());
        }

        [Theory]
        [InlineData(typeof(System.Reflection.TypeInfo))]
        [InlineData(typeof(System.Reflection.PropertyInfo))]
        [InlineData(typeof(System.Reflection.Emit.AssemblyBuilder))]
        public void Framework_subclass_in_a_nested_system_namespace_is_detected(Type type)
        {
            Assert.StartsWith("System.", type.Namespace);

            Assert.True(type.IsUnsafeToWalk());
        }

        // The collection is not a denied type. The validator enumerates it, and each item is then
        // checked on its own. Only the properties that the denied types declare are skipped.
        [Theory]
        [InlineData(typeof(Uri[]))]
        [InlineData(typeof(List<Uri>))]
        [InlineData(typeof(Type[]))]
        [InlineData(typeof(Dictionary<string, Uri>))]
        public void Collection_of_a_denied_type_is_not_detected(Type type)
        {
            Assert.False(type.IsUnsafeToWalk());
        }

        [Fact]
        public void Property_declared_by_a_denied_type_is_not_walked()
        {
            Assert.False(typeof(Uri).GetProperty(nameof(Uri.Segments)).IsWalked());
            Assert.False(typeof(UserUri).GetProperty(nameof(Uri.Segments)).IsWalked());
            Assert.False(typeof(Type).GetProperty(nameof(Type.DeclaringMethod)).IsWalked());
            Assert.False(typeof(System.IO.DirectoryInfo).GetProperty(nameof(System.IO.DirectoryInfo.Root)).IsWalked());
        }

        [Fact]
        public void Property_added_by_a_user_subclass_of_a_denied_type_is_walked()
        {
            Assert.True(typeof(UserUri).GetProperty(nameof(UserUri.Owner)).IsWalked());
        }

        public record BaseRecord;

        public record DerivedRecord : BaseRecord;

        public class KeyedById
        {
            public int Id { get; set; }

            public override bool Equals(object obj) => obj is KeyedById other && other.Id == Id;
            public override int GetHashCode() => Id;
        }

        // Inherits the override from KeyedById.
        public class DerivedKeyedById : KeyedById
        {
        }

        // A record overrides Equals in generated code. A struct inherits ValueType.Equals,
        // which overrides object.Equals and compares the fields.
        // See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record#value-equality
        // See: https://learn.microsoft.com/dotnet/api/system.valuetype.equals
        [Theory]
        [InlineData(typeof(BaseRecord), true)]
        [InlineData(typeof(DerivedRecord), true)]
        [InlineData(typeof(KeyedById), true)]
        [InlineData(typeof(DerivedKeyedById), true)]
        [InlineData(typeof(PlainPoint), true)]
        [InlineData(typeof(string), true)]
        [InlineData(typeof(Child), false)]
        [InlineData(typeof(SealedTag), false)]
        [InlineData(typeof(object), false)]
        public void Equals_override_is_detected(Type type, bool overridesEquals)
        {
            Assert.Equal(overridesEquals, type.OverridesEquals());
        }
    }
}
