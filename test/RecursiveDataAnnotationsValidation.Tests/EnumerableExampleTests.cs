using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class EnumerableExampleTests
    {
        private readonly IRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();
        
        [Fact]
        public void Passes_all_validation_no_children()
        {
            var model = new EnumerableExample
            {
                Name = "Passes all, no children nodes",
                Age = 18,
                Items = new List<ItemExample>(),
                ItemsList = new List<ItemExample>(),
                ItemsCollection = new List<ItemExample>(),
                ItemWithListExamples = new List<ItemWithListExample>(),
            };

            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }
        
        [Fact]
        public void Passes_all_validation_with_children()
        {
            var model = new EnumerableExample
            {
                Name = "Passes all with children nodes",
                Age = 128,
                Items = new List<ItemExample>
                {
                    new ItemExample
                    {
                        Name = "Child 1",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 1225,
                            StringB = "child-1-stringB",
                            BoolC = true,
                            ExampleEnumD = ExampleEnum.ValueC
                        }
                    },
                    new ItemExample
                    {
                        Name = "Child 2",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 15,
                            StringB = "child-2-string-abc",
                            BoolC = false,
                            ExampleEnumD = ExampleEnum.ValueA
                        }
                    }
                },
                ItemsList = new List<ItemExample>
                {
                    new ItemExample
                    {
                        Name = "Child 1L",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 123,
                            StringB = "child-1L-stringB",
                            BoolC = true,
                            ExampleEnumD = ExampleEnum.ValueC
                        }
                    },
                    new ItemExample
                    {
                        Name = "Child 2L",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 75,
                            StringB = "child-2L-string-abc",
                            BoolC = false,
                            ExampleEnumD = ExampleEnum.ValueA
                        }
                    }
                },
                ItemsCollection = new List<ItemExample>
                {
                    new ItemExample
                    {
                        Name = "Child 1C",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 25,
                            StringB = "child-1C-stringB",
                            BoolC = true,
                            ExampleEnumD = ExampleEnum.ValueC
                        }
                    },
                    new ItemExample
                    {
                        Name = "Child 2C",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 120,
                            StringB = "child-2C-string-abc",
                            BoolC = false,
                            ExampleEnumD = ExampleEnum.ValueA
                        }
                    }
                },
                ItemWithListExamples = new List<ItemWithListExample>
                {
                    new ItemWithListExample
                    {
                        ItemWithListName = "ItemList 0L",
                        Claims = new List<string>
                        {
                            "Claim 0L-1",
                            "Claim 0L-2",
                        },
                    },
                },
            };

            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }
        
        [Fact]
        public void Fails_on_Items_Child2_SimpleA_BoolC()
        {
            var model = new EnumerableExample
            {
                Name = "Fails_on_Items_Child2_SimpleA_BoolC",
                Age = 6,
                Items = new List<ItemExample>
                {
                    new ItemExample
                    {
                        Name = "Child 1",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 125,
                            StringB = "child-1-stringB",
                            BoolC = true,
                            ExampleEnumD = ExampleEnum.ValueC
                        }
                    },
                    new ItemExample
                    {
                        Name = "Child 2",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 153,
                            StringB = "child-2-string-abc",
                            BoolC = null, // failure
                            ExampleEnumD = ExampleEnum.ValueA
                        }
                    }
                }
            };

            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(model, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
            AssertThereIsAnErrorForMemberName("Items[1].SimpleA.BoolC", validationResults);
        }
        
        private readonly EnumerableExample _multipleFailureModel = new EnumerableExample
        {
            Name = "Multiple failures",
            Age = -5,
            Items = new List<ItemExample>
            {
                new ItemExample
                {
                    Name = "Child 0E",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 65,
                        StringB = "child-0E-stringB",
                        BoolC = true,
                        ExampleEnumD = ExampleEnum.ValueB
                    }
                },
                new ItemExample
                {
                    Name = "Child 1E",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 125,
                        StringB = "child-1E-stringB",
                        BoolC = null,
                        ExampleEnumD = ExampleEnum.ValueC
                    }
                },
                new ItemExample
                {
                    Name = "Child 2E",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 15,
                        StringB = "child-2E-string-abc",
                        BoolC = false,
                        ExampleEnumD = ExampleEnum.ValueA
                    }
                },
            },
            ItemsList = new List<ItemExample>
            {
                new ItemExample
                {
                    Name = "Child 0L",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 1423,
                        StringB = "child-0L-stringB",
                        BoolC = true,
                        ExampleEnumD = ExampleEnum.ValueC
                    }
                },
                new ItemExample
                {
                    Name = "Child 1L",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 13,
                        StringB = "child-1L-stringB",
                        BoolC = true,
                        ExampleEnumD = null
                    }
                },
                new ItemExample
                {
                    Name = "Child 2L",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 75,
                        StringB = "child-2L-string-abc",
                        BoolC = false,
                        ExampleEnumD = ExampleEnum.ValueA
                    }
                },
            },
            ItemsCollection = new List<ItemExample>
            {
                new ItemExample
                {
                    Name = "Child 0C",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = null,
                        StringB = "child-0C-stringB",
                        BoolC = true,
                        ExampleEnumD = null
                    }
                },
                new ItemExample
                {
                    Name = null,
                    SimpleA = new SimpleExample
                    {
                        IntegerA = null,
                        StringB = "child-1C-string-abc",
                        BoolC = false,
                        ExampleEnumD = ExampleEnum.ValueA
                    }
                },
                new ItemExample
                {
                    Name = "Child 2C",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 250,
                        StringB = "child-2C-stringB",
                        BoolC = null,
                        ExampleEnumD = null
                    }
                },
                new ItemExample
                {
                    Name = "Child 3C",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = null,
                        StringB = null,
                        BoolC = true,
                        ExampleEnumD = ExampleEnum.ValueA
                    }
                },
            },
            ItemWithListExamples = new List<ItemWithListExample>
            {
                new ItemWithListExample
                {
                    ItemWithListName = null,
                    Claims = new List<string>
                    {
                        null,
                        "ItemList Claim 0L-1L",
                    },
                },
                new ItemWithListExample
                {
                    ItemWithListName = "ItemList 1L",
                    Claims = new List<string>
                    {
                        "ItemList Claim 1L-0L",
                        " ", // whitespace
                    },
                },
                new ItemWithListExample
                {
                    ItemWithListName = "ItemList 2L",
                    Claims = null
                },
                new ItemWithListExample
                {
                    ItemWithListName = "ItemList 3L",
                    Claims = new List<string>
                    {
                        "ItemList Claim 3L-0L",
                        "ItemList Claim 3L-1L",
                        "ItemList Claim 3L-2L",
                        "ItemList Claim 3L-3L",
                        "ItemList Claim 3L-4L",
                        "ItemList Claim 3L-5L",
                    },
                },
            },
        };
        
        [Theory]
        [InlineData("Age")]
        [InlineData("Items[1].SimpleA.BoolC")]
        [InlineData("ItemsCollection[1].Name")]
        [InlineData("ItemsCollection[0].SimpleA.ExampleEnumD")]
        [InlineData("ItemsCollection[1].SimpleA.IntegerA")]
        [InlineData("ItemsCollection[2].SimpleA.BoolC")]
        [InlineData("ItemsCollection[2].SimpleA.ExampleEnumD")]
        [InlineData("ItemsCollection[3].SimpleA.IntegerA")]
        [InlineData("ItemsCollection[3].SimpleA.StringB")]
        [InlineData("ItemsList[1].SimpleA.ExampleEnumD")]
        [InlineData("ItemWithListExamples[0].ItemWithListName")]
        // It would be nice if these would indicate which element in the IEnumerable was not valid.
        // That's not easy / almost impossible to do with simple IEnumerable<T> like "string".
        // There might be an ValidationAttribute derived class that does it?
        // It may be out of scope for the RecursiveDataAnnotationsValidation project.
        // It might be possible to report on the FIRST failure in the IEnumerable<T> with the index,
        // but that would be the responsibility of whatever inherits from ValidationAttribute.
        [InlineData("ItemWithListExamples[0].Claims")]
        [InlineData("ItemWithListExamples[1].Claims")]
        // ^---- future would output "ItemWithListExamples[x].Claims[y]" for the specific element
        [InlineData("ItemWithListExamples[2].Claims")]
        public void Multiple_failures_contains_expected_memberName(string expectedMemberName)
        {
            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(_multipleFailureModel, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
            AssertThereIsAnErrorForMemberName(expectedMemberName, validationResults);
        }

        private void AssertThereIsAnErrorForMemberName(
            string expectedMemberName,
            IEnumerable<ValidationResult> validationResults
            )
        {
            var memberNames = validationResults
                .SelectMany(x => x.MemberNames)
                .OrderBy(x => x)
                .ToList();
            Assert.Contains(expectedMemberName, memberNames);
        }
    }
}