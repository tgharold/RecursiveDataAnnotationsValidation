using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class EnumerableExample
    {
        [Required]
        public string Name { get; set; }
        
        [Required]
        [Range(1,150)]
        public int? Age { get; set; }

        [Required]
        public IEnumerable<ItemExample> Items { get; set; }
        
        [Required]
        public List<ItemExample> ItemsList { get; set; }
        
        [Required]
        public List<ItemExample> ItemsCollection { get; set; }
        
        [Required]
        public List<ItemWithListExample> ItemWithListExamples { get; set; }
    }
}