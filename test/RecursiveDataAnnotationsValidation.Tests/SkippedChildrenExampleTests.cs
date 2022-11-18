using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class SkippedChildrenExampleTests
    {
        private readonly IRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();

        [Fact]
        public void Passes_all_validation()
        {
            var model = new SkippedChildrenExample
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
            var result = _sut.TryValidateObjectRecursive(model, validationResults);
            
            Assert.True(result);
            Assert.Empty(validationResults);
        }
        
        [Fact]
        public void Fails_for_SimpleA_BoolC()
        {
            var model = new SkippedChildrenExample
            {
                Name = "Skipped-Children-2",
                SimpleA = new SimpleExample
                {
                    IntegerA = 75124,
                    BoolC = null, // set one of the props to null
                    StringB = "simple-a-child-2",
                    ExampleEnumD = ExampleEnum.ValueC
                },
                SimpleB = new SimpleExample
                {
                    BoolC = true                    
                }
            };
            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(model, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains("SimpleA.BoolC")));            
        }
        
        [Fact]
        public void Fails_for_SimpleB_missing()
        {
            var model = new SkippedChildrenExample
            {
                Name = "Skipped-Children-2",
                SimpleA = new SimpleExample
                {
                    IntegerA = 75124,
                    BoolC = null,
                    StringB = "simple-a-child-2",
                    ExampleEnumD = ExampleEnum.ValueC
                },
                SimpleB = null // the object is missing entirely
            };
            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(model, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains("SimpleB")));            
        }
    }
}