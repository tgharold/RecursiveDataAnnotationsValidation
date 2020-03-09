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
        public void Can_handle_property_name_on_null_object()
        {
            object sut = null;
            var result = sut.GetPropertyValue("someProperty");
            Assert.Null(result);
        }

        [Fact]
        public void Get_null_for_bogus_property_name()
        {
            var sut = new { PropA = 5 };
            var result = sut.GetPropertyValue("doesNotExist");
            Assert.Null(result);
        }
        
        [Fact]
        public void Get_result_for_int_property()
        {
            var sut = new { PropA = 5 };
            var result = sut.GetPropertyValue(nameof(sut.PropA));
            Assert.Equal(sut.PropA, result);
        }
        
        [Fact]
        public void Get_result_for_string_property()
        {
            var sut = new { PropA = "XYZ" };
            var result = sut.GetPropertyValue(nameof(sut.PropA));
            Assert.Equal(sut.PropA, result);
        }
    }
}