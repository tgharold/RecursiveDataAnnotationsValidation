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
    /// - A nested result that names no member is an error of the whole object, such as one from
    ///   a class-level attribute or from IValidatableObject.Validate. Since 3.0 its member name is
    ///   the path of the object: "Lines[1]". Before 3.0 it had no member names, so a caller could
    ///   not tell which object failed. See ObjectLevelResults.
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
        // - A dictionary value is reported by its key: NoteMap[first]. Before 3.0 it was reached
        //   through ".Value", and the index was the position in enumeration order.
        // - A result with two member names gets the prefix on each of them.
        // - A result with no member names gets the path of the item as its member name.
        //   Before 3.0 it had no path at all.
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
                "NoteMap[first].Text | The Text field is required.",
                "Ranges[0].Low,Ranges[0].High | Low must not exceed High.",
                "Ranges[0] | The range is negative.",
            };
            var actual = results.Select(r => $"{string.Join(",", r.MemberNames)} | {r.ErrorMessage}");
            Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
        }

        /// <summary>
        /// Results that belong to a whole object and not to one of its members. Validator gives
        /// such a result no member names when it comes from a class-level ValidationAttribute.
        /// IValidatableObject.Validate gives none when it calls the ValidationResult constructor
        /// with only a message. A class-level attribute that passes ValidationContext.MemberName
        /// gives a null name, because no member is being validated, and some code passes "".
        /// All of these mean "this object". On a nested object, each of them is reported with the
        /// path of the object as its only member name, as MVC keys a model-level error by the
        /// prefix of the model. A result of the root object keeps its member names, because the
        /// root has no path.
        /// Before 3.0 a nested result with no member names had none, and a null or empty name gave
        /// the path with a dot and nothing after it, such as "Value[0].".
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationresult.-ctor
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.membername
        /// </summary>
        public class ObjectLevelResults
        {
            public class SelfValidating : IValidatableObject
            {
                public string[] MemberNames { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield return MemberNames == null
                        ? new ValidationResult("The object is not valid.")
                        : new ValidationResult("The object is not valid.", MemberNames);
                }
            }

            public class Holder
            {
                public SelfValidating Inner { get; set; }

                public List<SelfValidating> Items { get; set; }
            }

            private static List<string> Run(object model)
            {
                var results = new List<ValidationResult>();
                Assert.False(new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results));
                return ResultText.Describe(results);
            }

            [Fact]
            public void Result_with_no_member_names_gets_the_path_of_the_object()
            {
                var errors = Run(new Holder { Inner = new SelfValidating() });

                Assert.Equal(ResultText.Expect("Inner | The object is not valid."), errors);
            }

            [Fact]
            public void Result_with_no_member_names_on_an_item_gets_the_path_of_the_item()
            {
                var errors = Run(new Holder { Items = new List<SelfValidating> { null, new SelfValidating() } });

                Assert.Equal(ResultText.Expect("Items[1] | The object is not valid."), errors);
            }

            [Fact]
            public void Result_with_no_member_names_on_an_item_of_a_root_collection_gets_its_index()
            {
                var errors = Run(new List<SelfValidating> { new SelfValidating() });

                Assert.Equal(ResultText.Expect("[0] | The object is not valid."), errors);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Null_or_empty_member_name_gets_the_path_of_the_object(string name)
            {
                var errors = Run(new Holder { Inner = new SelfValidating { MemberNames = new[] { name } } });

                Assert.Equal(ResultText.Expect("Inner | The object is not valid."), errors);
            }

            // A result that names a member and the object at once keeps both, each with the path.
            [Fact]
            public void Named_and_unnamed_members_in_one_result_each_get_the_path()
            {
                var errors = Run(new Holder { Inner = new SelfValidating { MemberNames = new[] { "Low", null } } });

                Assert.Equal(ResultText.Expect("Inner.Low,Inner | The object is not valid."), errors);
            }

            // Guard. The root object has no path, so its results keep their member names.
            [Fact]
            public void Result_of_the_root_object_keeps_no_member_names()
            {
                var errors = Run(new SelfValidating());

                Assert.Equal(ResultText.Expect(" | The object is not valid."), errors);
            }
        }
    }
}
