using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// Guards the shape of the results, which callers may parse or show to users:
    /// - A nested member name joins property names with dots: "Customer.Address.Zip".
    /// - A collection item adds its index, counted from 0: "Lines[1].Notes[2].Text".
    /// - The error message is the one the attribute produced on the nested object, unchanged,
    ///   so it names the nested property only ("The Text field is required."), not the path.
    /// - Each error appears once, with one member name.
    /// The test compares the complete set of results, so an extra, missing or reworded result
    /// fails it. It sorts both sides first, because reflection does not promise a property order.
    /// See: https://learn.microsoft.com/dotnet/api/system.type.getproperties
    /// The messages are the framework's default English messages for each attribute.
    /// </summary>
    public class MemberNameFormatTests
    {
        public class Order
        {
            [Required]
            public string Number { get; set; }

            public Customer Customer { get; set; }

            public List<Line> Lines { get; set; }
        }

        public class Customer
        {
            [Required]
            public string Name { get; set; }

            public Address Address { get; set; }
        }

        public class Address
        {
            [StringLength(5)]
            public string Zip { get; set; }
        }

        public class Line
        {
            [Range(1, 10)]
            public int Quantity { get; set; }

            public Product Product { get; set; }

            public List<Note> Notes { get; set; }
        }

        public class Product
        {
            [Required]
            public string Sku { get; set; }
        }

        public class Note
        {
            [Required]
            public string Text { get; set; }
        }

        [Fact]
        public void Nested_results_keep_their_member_names_and_messages()
        {
            var order = new Order
            {
                Number = null,
                Customer = new Customer { Name = "Ann", Address = new Address { Zip = "123456" } },
                Lines = new List<Line>
                {
                    new Line { Quantity = 1, Product = new Product { Sku = null } },
                    new Line
                    {
                        Quantity = 0,
                        Product = new Product { Sku = "A" },
                        Notes = new List<Note> { new Note { Text = "a" }, new Note { Text = "b" }, new Note() },
                    },
                },
            };

            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(order, results);

            Assert.False(valid);
            var expected = new[]
            {
                "Number | The Number field is required.",
                "Customer.Address.Zip | The field Zip must be a string with a maximum length of 5.",
                "Lines[0].Product.Sku | The Sku field is required.",
                "Lines[1].Quantity | The field Quantity must be between 1 and 10.",
                "Lines[1].Notes[2].Text | The Text field is required.",
            };
            var actual = results.Select(r => $"{string.Join(",", r.MemberNames)} | {r.ErrorMessage}");
            Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
        }

        /// <summary>
        /// Validator calls Validate() on any object that implements IValidatableObject, and keeps
        /// the member names that Validate() returns.
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.ivalidatableobject
        /// </summary>
        public class Bounds : IValidatableObject
        {
            public int Low { get; set; }
            public int High { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                // One result that names two members.
                if (Low > High) yield return new ValidationResult("Low must not exceed High.", new[] { nameof(Low), nameof(High) });

                // A type-level result that names no member.
                if (Low < 0) yield return new ValidationResult("The range is negative.");
            }
        }

        public class Container
        {
            public Note[] NoteArray { get; set; }
            public List<Note> NoteList { get; set; }
            public Dictionary<string, Note> NoteMap { get; set; }
            public List<Bounds> Ranges { get; set; }
        }

        // Each case below shows one rule for building the member name:
        // - An array item gets an index like a list item.
        // - A null item is skipped but still uses up its index: NoteList[1], not NoteList[0].
        // - A dictionary item is a KeyValuePair, so its value is reached through ".Value", and the
        //   index is the position in enumeration order, not the key.
        // - A result with two member names gets the prefix on each of them.
        // - A result with no member names has no path at all once nested. This is a known gap
        //   (see ValidatorHardeningTests.PrimitiveCollections). If it is fixed, this guard fails
        //   on purpose, because the format callers see changes.
        // See: https://learn.microsoft.com/dotnet/api/system.collections.generic.keyvaluepair-2
        [Fact]
        public void Collection_items_keep_their_member_names()
        {
            var container = new Container
            {
                NoteArray = new[] { new Note() },
                NoteList = new List<Note> { null, new Note() },
                NoteMap = new Dictionary<string, Note> { ["first"] = new Note() },
                Ranges = new List<Bounds> { new Bounds { Low = -1, High = -5 } },
            };

            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(container, results);

            Assert.False(valid);
            var expected = new[]
            {
                "NoteArray[0].Text | The Text field is required.",
                "NoteList[1].Text | The Text field is required.",
                "NoteMap[0].Value.Text | The Text field is required.",
                "Ranges[0].Low,Ranges[0].High | Low must not exceed High.",
                " | The range is negative.",
            };
            var actual = results.Select(r => $"{string.Join(",", r.MemberNames)} | {r.ErrorMessage}");
            Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
        }
    }
}
