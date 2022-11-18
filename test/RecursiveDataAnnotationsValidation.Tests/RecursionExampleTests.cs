using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class RecursionExampleTests
    {
        private readonly IRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();
        
        // Verify that we can recursively validate, but avoid infinite loops
        
        [Fact]
        public void Passes_all_validation()
        {
            var recursiveModel = new RecursionExample
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
            recursiveModel.Recursion.Recursion = recursiveModel;
            
            var model = new RecursionExample
            {
                Name = "SUT",
                BooleanA = true,
                Recursion = recursiveModel
            };
            
            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(model, validationResults);
            
            Assert.True(result);
            Assert.Empty(validationResults);
        }        
        
        [Fact]
        public void Fails_because_inner1_boolean_is_null()
        {
            var recursiveModel = new RecursionExample
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
            recursiveModel.Recursion.Recursion = recursiveModel;
            
            var model = new RecursionExample
            {
                Name = "SUT",
                BooleanA = true,
                Recursion = recursiveModel
            };
            
            var validationResults = new List<ValidationResult>();
            var result = _sut.TryValidateObjectRecursive(model, validationResults);
            
            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains("Recursion.Recursion.BooleanA")));
        }        
    }
}