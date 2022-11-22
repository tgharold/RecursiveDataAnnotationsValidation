using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using RecursiveDataAnnotationsValidation.Tests.Attributes;

namespace RecursiveDataAnnotationsValidation.Tests.TestModels
{
    public class ItemWithListExample
    {
        [Required]
        public string ItemWithListName { get; set; }
        
        [Required]
        [EnumerableStringNotNullOrWhitespace]
        public List<string> Claims { get; set; }
    }
}