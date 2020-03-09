using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class RecursiveExample
    {
        [Required]
        public string Name { get; set; }
        
        [Required]
        public bool? BooleanA { get; set; }
        
        [Required]
        public RecursiveExample Recursive { get; set; }
    }
}