using System.ComponentModel.DataAnnotations;
using RecursiveDataAnnotationsValidation.Attributes;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class SkippedChildrenExample
    {
        [Required]
        public string Name { get; set; }
        
        [Required]
        public SimpleExample SimpleA { get; set; }
        
        [Required]
        [SkipRecursiveValidation]
        public SimpleExample SimpleB { get; set; }
    }
}