using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class EnumerableExample
    {
        [Required]
        public string Name { get; set; }
        
        [Required]
        public int? Age { get; set; }

        [Required]
        public IEnumerable<ItemExample> Items { get; set; }
    }
}