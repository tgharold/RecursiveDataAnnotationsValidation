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
    }
}
