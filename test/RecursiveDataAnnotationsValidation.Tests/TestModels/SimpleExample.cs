using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class SimpleExample
    {
        [Required]
        public int? IntegerA { get; set; }
        
        [Required]
        public string StringB { get; set; }
        
        [Required]
        public bool? BoolC { get; set; }
        
        [Required]
        public ExampleEnum? ExampleEnumD { get; set; }        
    }
}