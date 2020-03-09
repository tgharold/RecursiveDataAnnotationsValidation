using RecursiveDataAnnotationsValidation.Extensions;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests.Extensions
{
    public class ObjectExtensionsTests
    {
        [Fact]
        public void Can_handle_null_object()
        {
            object sut = null;
            var result = sut.GetPropertyValue(null);
            Assert.Null(result);
        }
        
        [Fact]
        public void Can_handle_property_name_onnull_object()
        {
            object sut = null;
            var result = sut.GetPropertyValue("someProperty");
            Assert.Null(result);
        }
    }
}