using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class AsyncValidatorTests
    {
        private readonly IAsyncRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();

        [Fact]
        public async Task Pass_all_validation()
        {
            var model = new SimpleExample
            {
                IntegerA = 100,
                StringB = "test-100",
                BoolC = true,
                ExampleEnumD = ExampleEnum.ValueB
            };

            var validationContext = new ValidationContext(model);
            var validationResults = new List<ValidationResult>();
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationContext, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }

        [Fact]
        public async Task Indicate_that_IntegerA_is_missing()
        {
            var model = new SimpleExample
            {
                IntegerA = null,
                StringB = "test-101",
                BoolC = false,
                ExampleEnumD = ExampleEnum.ValueC
            };

            const string fieldName = nameof(SimpleExample.IntegerA);
            var validationContext = new ValidationContext(model);
            var validationResults = new List<ValidationResult>();
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationContext, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
            Assert.NotNull(validationResults
                .FirstOrDefault(x => x.MemberNames.Contains(fieldName)));
        }

        [Fact]
        public async Task Pass_all_validation_without_context()
        {
            var model = new SimpleExample
            {
                IntegerA = 100,
                StringB = "test-100",
                BoolC = true,
                ExampleEnumD = ExampleEnum.ValueB
            };

            var validationResults = new List<ValidationResult>();
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }
    }
}