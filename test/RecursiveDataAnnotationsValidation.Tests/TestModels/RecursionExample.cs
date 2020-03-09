using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class RecursionExample
    {
        [Required]
        public string Name { get; set; }
        
        [Required]
        public bool? BooleanA { get; set; }
        
        [Required]
        public RecursionExample Recursion { get; set; }
    }
}