using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class DeadlockPreventionTests
    {
        private readonly IAsyncRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();

        /// <summary>
        /// Test to ensure that our async implementation doesn't create potential deadlocks
        /// by testing proper async execution patterns.
        /// </summary>
        [Fact]
        public async Task Async_methods_should_not_create_deadlocks()
        {
            // This test ensures that the async methods properly execute
            // without creating synchronization context issues that could lead to deadlocks

            var model = new SimpleExample
            {
                IntegerA = 100,
                StringB = "test-100",
                BoolC = true,
                ExampleEnumD = ExampleEnum.ValueB
            };

            var validationResults = new List<ValidationResult>();

            // Test that the async method can be awaited without blocking
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }

        /// <summary>
        /// Test that validates async behavior with collections doesn't cause blocking issues
        /// </summary>
        [Fact]
        public async Task Async_collection_validation_should_not_deadlock()
        {
            var model = new ItemWithListExample
            {
                ItemWithListName = "Parent",
                Claims = new List<string> { "Claim1", "Claim2" }
            };

            var validationResults = new List<ValidationResult>();

            // Test that collection validation works async without hanging
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }

        /// <summary>
        /// Test that validates recursive structure handling async without deadlock
        /// </summary>
        [Fact]
        public async Task Async_recursive_validation_should_not_deadlock()
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

            // Test that recursive validation works async without hanging
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }

        /// <summary>
        /// Test that the async methods properly handle validation errors without deadlocking
        /// </summary>
        [Fact]
        public async Task Async_error_handling_should_not_deadlock()
        {
            var model = new SimpleExample
            {
                IntegerA = null, // This should fail validation due to [Required]
                StringB = "test-101",
                BoolC = false,
                ExampleEnumD = ExampleEnum.ValueC
            };

            var validationResults = new List<ValidationResult>();

            // Test that error handling works async without hanging
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
        }

        /// <summary>
        /// Test that validates proper async execution pattern for concurrent usage
        /// </summary>
        [Fact]
        public async Task Multiple_concurrent_async_calls_should_not_deadlock()
        {
            var model1 = new SimpleExample
            {
                IntegerA = 100,
                StringB = "test-100",
                BoolC = true,
                ExampleEnumD = ExampleEnum.ValueB
            };

            var model2 = new SimpleExample
            {
                IntegerA = 200,
                StringB = "test-200",
                BoolC = false,
                ExampleEnumD = ExampleEnum.ValueA
            };

            var validationResults1 = new List<ValidationResult>();
            var validationResults2 = new List<ValidationResult>();

            // Test concurrent async calls
            var task1 = _sut.TryValidateObjectRecursiveAsync(model1, validationResults1);
            var task2 = _sut.TryValidateObjectRecursiveAsync(model2, validationResults2);

            // Both should complete without deadlocking
            var results = await Task.WhenAll(task1, task2);

            Assert.True(results[0]);
            Assert.True(results[1]);
            Assert.Empty(validationResults1);
            Assert.Empty(validationResults2);
        }
    }
}