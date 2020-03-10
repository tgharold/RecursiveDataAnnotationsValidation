using System.ComponentModel.DataAnnotations;

namespace OptionsPatternMvc.Example.Settings
{
    public class ExampleAppSettings
    {
        [Required]
        public string Name { get; set; }
    }
}