using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class ItemExample
    {
        [Required]
        public string Name { get; set; }
        
        [Required]
        public SimpleExample SimpleA { get; set; }
    }
}