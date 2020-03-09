using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class EnumerableExampleTests
    {
        private readonly IRecursiveDataAnnotationValidator _validator = new RecursiveDataAnnotationValidator();
        
        [Fact]
        public void Passes_all_validation_no_children()
        {
            var sut = new EnumerableExample
            {
                Name = "Passes all",
                Age = 75,
                Items = new List<ItemExample>()
            };

            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }
        
        [Fact]
        public void Passes_all_validation_with_children()
        {
            var sut = new EnumerableExample
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
                }
            };

            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }
        
        [Fact]
        public void Fails_on_Items_Child2_SimpleA_BoolC()
        {
            var sut = new EnumerableExample
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
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains("Items.SimpleA.BoolC")));
        }
    }
}