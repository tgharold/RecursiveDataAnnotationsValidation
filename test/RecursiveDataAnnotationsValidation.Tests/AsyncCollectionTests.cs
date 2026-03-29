using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Tests.TestModels;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    public class AsyncCollectionTests
    {
        private readonly IAsyncRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();

        [Fact]
        public async Task Validates_collections_recursively()
        {
            var model = new ItemWithListExample
            {
                ItemWithListName = "Parent",
                Claims = new List<string> { "Claim1", "Claim2" }
            };

            var validationResults = new List<ValidationResult>();
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.True(result);
            Assert.Empty(validationResults);
        }

        [Fact]
        public async Task Fails_when_collection_item_has_validation_errors()
        {
            var model = new ItemWithListExample
            {
                ItemWithListName = "Parent",
                Claims = new List<string> { null } // This should fail validation due to [Required]
            };

            var validationResults = new List<ValidationResult>();
            var result = await _sut.TryValidateObjectRecursiveAsync(model, validationResults);

            Assert.False(result);
            Assert.NotEmpty(validationResults);
        }
    }
}