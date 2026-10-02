using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// A struct that the validator reaches by two routes. A class object is reported once, at the
    /// shortest path. A struct is reported once too, wherever the validator can tell that the two
    /// routes lead to the same struct.
    ///
    /// Why it is hard. The validator remembers each object it has validated, by reference, so that
    /// a shared object or a cycle is not walked again (see ObjectReferenceComparer). A class
    /// instance has one identity. A struct has none: it is copied each time it is read from a
    /// property, and each time an enumerator returns it. The validator holds it as an object, which
    /// boxes the copy into a new object. Two boxed copies of the same struct are never the same
    /// reference, so the second one looks new.
    /// See: https://learn.microsoft.com/dotnet/csharp/programming-guide/types/boxing-and-unboxing
    ///
    /// How 3.0 avoids most of the routes:
    /// - Each collection is enumerated once. The collection itself is an object with an identity,
    ///   so an array that two properties hold yields its structs once, at the shortest path. A
    ///   collection that a second route reaches as an item is still validated as an object there,
    ///   so its own attributes run, but it is not enumerated again.
    /// - A collection that is an item or the root object is enumerated, and the properties that a
    ///   framework type declares on it are not walked, as for a collection that a property holds.
    ///   Those properties repeat the items: ArraySegment&lt;T&gt;.Array, LinkedList&lt;T&gt;.First,
    ///   Dictionary&lt;TKey, TValue&gt;.Values. The properties that a collection type of your own adds
    ///   are still walked.
    /// See: https://learn.microsoft.com/dotnet/api/system.arraysegment-1.array
    ///
    /// What is left. Two properties that each return a struct, such as a property and a computed
    /// copy of it, give two separate values. The validator reports each one. Mark the copy with
    /// [SkipRecursiveValidation] to report it once.
    /// </summary>
    public class StructsReachedByTwoRoutesTests
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

        public class LineAndCopy
        {
            public Line Line { get; set; }

            public Line Copy => Line;
        }

        // A collection type of your own, with an attribute on a property it adds.
        public class PagedLines : List<Line>
        {
            [Required]
            public string Cursor { get; set; }
        }

        public class ListInPropertyAndItem
        {
            public PagedLines Lines { get; set; }

            public List<object> Items { get; set; }
        }

        private const string TextRequired = " | The Text field is required.";

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

        // Guard. A class object that two properties hold is validated once.
        [Fact]
        public void Class_object_reached_by_two_properties_is_reported_once()
        {
            var shared = new List<Leaf> { new Leaf() };

            var (valid, errors) = Run(new TwoLists { First = shared, Second = shared });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("First[0].Name | The Name field is required."), errors);
        }

        // Spec. 2.3.3 reported Second[0].Text too, because it enumerated the array once for
        // each property.
        [Fact]
        public void Struct_in_an_array_that_two_properties_hold_is_reported_once()
        {
            var shared = new[] { new Line() };

            var (valid, errors) = Run(new TwoArrays { First = shared, Second = shared });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("First[0].Text" + TextRequired), errors);
        }

        // Spec. The list is reached through Lines and as an item of Items. It is enumerated once,
        // through the shorter path. As an item it is still validated as an object, so the Cursor
        // error is reported there.
        [Fact]
        public void Collection_in_a_property_and_in_an_item_is_enumerated_once_and_validated()
        {
            var lines = new PagedLines { new Line() };

            var (valid, errors) = Run(new ListInPropertyAndItem { Lines = lines, Items = new List<object> { lines } });

            Assert.False(valid);
            Assert.Equal(
                ResultText.Expect("Lines[0].Text" + TextRequired, "Items[0].Cursor | The Cursor field is required."),
                errors);
        }

        // Limitation guard. Each read of a struct property returns a copy, so Line and Copy are
        // two structs to the validator.
        [Fact]
        public void Struct_returned_by_two_properties_is_reported_twice()
        {
            var (valid, errors) = Run(new LineAndCopy { Line = new Line() });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Line.Text" + TextRequired, "Copy.Text" + TextRequired), errors);
        }

        // Spec. Before, the Array property gave a second route: Value[0].Array[0].Text.
        [Fact]
        public void Struct_in_an_array_segment_item_is_reported_once()
        {
            var segment = new ArraySegment<Line>(new[] { new Line() });

            var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { segment } });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Text" + TextRequired), errors);
        }

        // Guard. A struct that is valid adds no entry.
        [Fact]
        public void Valid_struct_in_an_array_segment_item_is_not_reported()
        {
            var segment = new ArraySegment<Line>(new[] { new Line { Text = "a" } });

            var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { segment } });

            Assert.True(valid);
            Assert.Empty(errors);
        }

        // Guard. Class objects were reported once before, too, at the index.
        [Fact]
        public void Class_objects_in_an_array_segment_item_are_reported_once()
        {
            var segment = new ArraySegment<Leaf>(new[] { new Leaf() });

            var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { segment } });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Name | The Name field is required."), errors);
        }

        // Spec. The segment holds only the first element of the array. The second one is not part
        // of the model, as when a property holds the segment. Before, Array reached it.
        [Fact]
        public void Object_outside_an_array_segment_item_is_not_validated()
        {
            var array = new[] { new Leaf { Name = "in the segment" }, new Leaf() };
            var segment = new ArraySegment<Leaf>(array, 0, 1);

            var (valid, errors) = Run(new Holder<List<object>> { Value = new List<object> { segment } });

            Assert.True(valid);
            Assert.Empty(errors);
        }

        // Spec. Before, First gave three more routes: First.List[0], First.Value and, from .NET 6
        // on, First.ValueRef.
        // See: https://learn.microsoft.com/dotnet/api/system.collections.generic.linkedlistnode-1.valueref
        [Fact]
        public void Struct_in_a_linked_list_item_is_reported_once()
        {
            var list = new LinkedList<Line>(new[] { new Line() });

            var (valid, errors) = Run(new Holder<List<LinkedList<Line>>> { Value = new List<LinkedList<Line>> { list } });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Text" + TextRequired), errors);
        }

        // Spec. The root object is walked like an item.
        [Fact]
        public void Struct_in_a_root_linked_list_is_reported_once()
        {
            var (valid, errors) = Run(new LinkedList<Line>(new[] { new Line() }));

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("[0].Text" + TextRequired), errors);
        }

        // Spec. Before, the Values property gave a second route: Value[0].Values[0].Text.
        [Fact]
        public void Struct_value_in_a_dictionary_item_is_reported_once()
        {
            var map = new Dictionary<string, Line> { ["a"] = new Line() };

            var (valid, errors) = Run(new Holder<List<Dictionary<string, Line>>> { Value = new List<Dictionary<string, Line>> { map } });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Value[0][0].Value.Text" + TextRequired), errors);
        }
    }
}
