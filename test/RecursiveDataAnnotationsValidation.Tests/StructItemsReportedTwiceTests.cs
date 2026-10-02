using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// A struct that the validator reaches by two routes is reported once for each route.
    /// A class object is reported once.
    ///
    /// Why. The validator remembers each object it has validated, by reference, so that a shared
    /// object or a cycle is not walked again (see ObjectReferenceComparer). A class instance has one
    /// identity. A struct has none: it is copied each time it is read from a property, and each
    /// time an enumerator returns it. The validator holds it as an object, which boxes the copy into
    /// a new object. Two boxed copies of the same struct are never the same reference, so the second
    /// one looks new and is validated again.
    /// See: https://learn.microsoft.com/dotnet/csharp/programming-guide/types/boxing-and-unboxing
    ///
    /// Where it happens.
    /// - Two properties hold the same array of structs. This is so in 2.3.3 too.
    /// - An item that is a collection returns its structs by enumeration and also through a public
    ///   property, such as ArraySegment&lt;T&gt;.Array. Since 2.4.0 the validator enumerates an item that
    ///   is a collection and also walks its properties, so such an item reports each struct twice.
    ///   Before, only the property route found them. A LinkedList&lt;T&gt; item reports each struct three
    ///   times, because its First node also reaches the list through two properties.
    /// See: https://learn.microsoft.com/dotnet/api/system.arraysegment-1.array
    ///
    /// What it is not. The invalid struct is reported, so no model passes that failed before. The
    /// result list has an extra entry with a different member name. A caller that shows the whole
    /// list shows the same error twice, and a caller that counts the errors counts too many.
    /// For a collection type of your own, mark the property that repeats the items with
    /// [SkipRecursiveValidation], and each struct is reported once. A framework type, such as
    /// ArraySegment, has no such property to mark.
    ///
    /// These are limitation guards. They pass today. If the validator learns to match a struct by
    /// more than reference, they fail on purpose, so the change is deliberate. A fix has to decide
    /// which of the paths to keep. The paths here start with the property route, which 2.3.3 reported.
    /// </summary>
    public class StructItemsReportedTwiceTests
    {
        public struct Line
        {
            [Required]
            public string Text { get; set; }
        }

        public class Leaf
        {
            [Required]
            public string Name { get; set; }
        }

        public class Holder<T>
        {
            public T Value { get; set; }
        }

        public class TwoArrays
        {
            public Line[] First { get; set; }
            public Line[] Second { get; set; }
        }

        public class TwoLists
        {
            public List<Leaf> First { get; set; }
            public List<Leaf> Second { get; set; }
        }

        private const string TextRequired = " | The Text field is required.";

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

        // Contrast. A class object that two properties hold is validated once.
        [Fact]
        public void Class_object_reached_by_two_properties_is_reported_once()
        {
            var shared = new List<Leaf> { new Leaf() };

            var (valid, errors) = Run(new TwoLists { First = shared, Second = shared });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("First[0].Name | The Name field is required."), errors);
        }

        // Also so in 2.3.3. The array is one object, but the validator enumerates the collection
        // that each property holds, and each enumeration boxes a new copy of the struct.
        [Fact]
        public void Struct_reached_by_two_properties_is_reported_twice()
        {
            var shared = new[] { new Line() };

            var (valid, errors) = Run(new TwoArrays { First = shared, Second = shared });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("First[0].Text" + TextRequired, "Second[0].Text" + TextRequired), errors);
        }

        // New in 2.4.0. 2.3.3 reports only the first path, through the Array property.
        [Fact]
        public void Struct_in_an_array_segment_item_is_reported_twice()
        {
            var segment = new ArraySegment<Line>(new[] { new Line() });

            var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { segment } });

            Assert.False(valid);
            Assert.Equal(
                ResultText.Expect("Value[0].Array[0].Text" + TextRequired, "Value[0][0].Text" + TextRequired),
                errors);
        }

        // A struct that is valid adds no entry for either route.
        [Fact]
        public void Valid_struct_in_an_array_segment_item_is_not_reported()
        {
            var segment = new ArraySegment<Line>(new[] { new Line { Text = "a" } });

            var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { segment } });

            Assert.True(valid);
            Assert.Empty(errors);
        }

        // The same item of class objects is reported once, because the second route finds that the
        // objects have been validated.
        [Fact]
        public void Class_objects_in_an_array_segment_item_are_reported_once()
        {
            var segment = new ArraySegment<Leaf>(new[] { new Leaf() });

            var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { segment } });

            Assert.False(valid);
            Assert.Single(errors);
        }

#if NET8_0_OR_GREATER
        // LinkedListNode.ValueRef is new in .NET 6, so this shape is not the same on .NET Framework.
        // First reaches the list through its List property, and the node through ValueRef.
        // See: https://learn.microsoft.com/dotnet/api/system.collections.generic.linkedlistnode-1.valueref
        [Fact]
        public void Struct_in_a_linked_list_item_is_reported_three_times()
        {
            var list = new LinkedList<Line>(new[] { new Line() });

            var (valid, errors) = Run(new Holder<List<LinkedList<Line>>> { Value = new List<LinkedList<Line>> { list } });

            Assert.False(valid);
            Assert.Equal(
                ResultText.Expect(
                    "Value[0].First.List[0].Text" + TextRequired,
                    "Value[0].First.ValueRef.Text" + TextRequired,
                    "Value[0][0].Text" + TextRequired),
                errors);
        }
#endif
    }
}
