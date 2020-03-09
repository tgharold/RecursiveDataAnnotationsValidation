using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class SkippedChildrenExampleTests
    {
        private readonly IRecursiveDataAnnotationValidator _validator = new RecursiveDataAnnotationValidator();

        [Fact]
        public void Passes_all_validation()
        {
            var sut = new SkippedChildrenExample
            {
                Name = "Skipped-Children-1",
                SimpleA = new SimpleExample
                {
                    IntegerA = 75123,
                    BoolC = false,
                    StringB = "simple-a-child-1",
                    ExampleEnumD = ExampleEnum.ValueC
                },
                SimpleB = new SimpleExample
                {
                    
                }
            };
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.True(result);
            Assert.Empty(validationResults);
        }
    }
}