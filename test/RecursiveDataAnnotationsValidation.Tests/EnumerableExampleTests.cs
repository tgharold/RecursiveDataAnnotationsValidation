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
                Name = "Passes all",
                Age = 75,
                Items = new List<ItemExample>(),
                ItemsList = new List<ItemExample>(),
                ItemsCollection = new List<ItemExample>(),
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
                Name = "Passes all",
                Age = 75,
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
                }
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
                Name = "Passes all",
                Age = 75,
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
                            IntegerA = 15,
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
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains("Items.SimpleA.BoolC")));
        }
        
        private readonly EnumerableExample _multipleFailureModel = new EnumerableExample
        {
            Name = "Passes all",
            Age = 75,
            Items = new List<ItemExample>
            {
                new ItemExample
                {
                    Name = "Child 3E",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 65,
                        StringB = "child-3E-stringB",
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
                    Name = "Child 3L",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = 13,
                        StringB = "child-3L-stringB",
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
                    Name = "Child 1C",
                    SimpleA = new SimpleExample
                    {
                        IntegerA = null,
                        StringB = "child-1C-stringB",
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
                        StringB = "child-2C-string-abc",
                        BoolC = false,
                        ExampleEnumD = ExampleEnum.ValueA
                    }
                },
            }
        };
        
        [Theory]
        [InlineData("Items.SimpleA.BoolC")]
        [InlineData("ItemsCollection.Name")]
        [InlineData("ItemsCollection.SimpleA.ExampleEnumD")]
        [InlineData("ItemsCollection.SimpleA.IntegerA")]
        [InlineData("ItemsList.SimpleA.ExampleEnumD")]
        public void Multiple_failures(string expectedMemberName)
        {
            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(_multipleFailureModel, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
            var memberNames = validationResults
                .SelectMany(x => x.MemberNames)
                .OrderBy(x => x)
                .ToList();
            Assert.Contains(expectedMemberName, memberNames);
        }
    }
}