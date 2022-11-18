using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class RecursiveDataAnnotationValidatorTests
    {
        /// <summary>Tests that use the method which takes a ValidationContext.</summary>
        public class ValidationContextTests
        {
            private readonly IRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();
            
            [Fact]
            public void Pass_all_validation()
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
                var result = _sut.TryValidateObjectRecursive(model, validationContext, validationResults);
            
                Assert.True(result);
                Assert.Empty(validationResults);
            }
            
            [Fact]
            public void Indicate_that_IntegerA_is_missing()
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
                var result = _sut.TryValidateObjectRecursive(model, validationContext, validationResults);
            
                Assert.False(result);
                Assert.NotEmpty(validationResults);
                Assert.NotNull(validationResults
                    .FirstOrDefault(x => x.MemberNames.Contains(fieldName)));
            }
        }
    }
}