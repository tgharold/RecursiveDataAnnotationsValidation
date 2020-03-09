using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class RecursionExampleTests
    {
        private readonly IRecursiveDataAnnotationValidator _validator = new RecursiveDataAnnotationValidator();
        
        // Verify that we can recursively validate, but avoid infinite loops
        
        [Fact]
        public void Passes_all_validation()
        {
            var recursion = new RecursionExample
            {
                Name = "Recursion1",
                BooleanA = false,
                Recursion = new RecursionExample
                {
                    Name = "Recursion1.Inner1",
                    BooleanA = true,
                    Recursion = null
                }
            };
            recursion.Recursion.Recursion = recursion;
            
            var sut = new RecursionExample
            {
                Name = "SUT",
                BooleanA = true,
                Recursion = recursion
            };
            
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.True(result);
            Assert.Empty(validationResults);
        }        
        
        [Fact]
        public void Fails_because_inner1_boolean_is_null()
        {
            var recursion = new RecursionExample
            {
                Name = "Recursion1",
                BooleanA = false,
                Recursion = new RecursionExample
                {
                    Name = "Recursion1.Inner1",
                    BooleanA = null,
                    Recursion = null
                }
            };
            recursion.Recursion.Recursion = recursion;
            
            var sut = new RecursionExample
            {
                Name = "SUT",
                BooleanA = true,
                Recursion = recursion
            };
            
            var validationResults = new List<ValidationResult>();
            var result = _validator.TryValidateObjectRecursive(sut, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains("Recursion.Recursion.BooleanA")));
        }        
    }
}