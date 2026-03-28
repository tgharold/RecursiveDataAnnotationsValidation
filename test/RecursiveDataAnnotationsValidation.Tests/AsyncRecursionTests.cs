using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class AsyncRecursionTests
    {
        private readonly IAsyncRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();

        // This test verifies that async version handles recursive structures correctly
        [Fact]
        public async Task Handles_recursive_structures_without_infinite_loop()
        {
            var recursiveModel = new RecursionExample
            {
                Name = "Recursion1-pass",
                BooleanA = false,
                Recursion = new RecursionExample
                {
                    Name = "Recursion1-pass.Inner1",
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
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }

        [Fact]
        public async Task Fails_when_recursive_property_has_validation_errors()
        {
            var recursiveModel = new RecursionExample
            {
                Name = "Recursion1-fail",
                BooleanA = false,
                Recursion = new RecursionExample
                {
                    Name = "Recursion1-fail.Inner1",
                    BooleanA = null, // This should fail validation due to [Required]
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
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
        }
    }
}