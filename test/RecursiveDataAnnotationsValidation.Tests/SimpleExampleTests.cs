using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class SimpleExampleTests
    {
        private readonly IRecursiveDataAnnotationValidator _validator = new RecursiveDataAnnotationValidator();

        [Fact]
        public void Passes_all_validation()
        {
            var sut = new SimpleExample
            {
                IntegerA = 100,
                StringB = "test-100",
                BoolC = true,
                ExampleEnumD = ExampleEnum.ValueB
            };
            
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.True(result);
            Assert.Empty(validationResults);
        }
        
        [Fact]
        public void Indicate_that_IntegerA_is_missing()
        {
            var sut = new SimpleExample
            {
                IntegerA = null,
                StringB = "test-101",
                BoolC = false,
                ExampleEnumD = ExampleEnum.ValueC
            };
            
            const string fieldName = nameof(SimpleExample.IntegerA);
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains(fieldName)));
        }
        
        [Fact]
        public void Indicate_that_StringB_is_missing()
        {
            var sut = new SimpleExample
            {
                IntegerA = 102,
                StringB = null,
                BoolC = true,
                ExampleEnumD = ExampleEnum.ValueA
            };
            
            const string fieldName = nameof(SimpleExample.StringB);
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains(fieldName)));
        }
        
        [Fact]
        public void Indicate_that_StringB_and_BoolC_are_missing()
        {
            var sut = new SimpleExample
            {
                IntegerA = 102,
                StringB = null,
                BoolC = null
            };
            
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains(nameof(SimpleExample.StringB))));
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains(nameof(SimpleExample.BoolC))));
        }
        
        [Fact]
        public void Indicate_that_IntegerA_and_ExampleEnumD_are_missing()
        {
            var sut = new SimpleExample
            {
                IntegerA = null,
                StringB = "test-106",
                BoolC = true,
                ExampleEnumD = null
            };
            
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains(nameof(SimpleExample.IntegerA))));
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains(nameof(SimpleExample.ExampleEnumD))));
        }
        
    }
}