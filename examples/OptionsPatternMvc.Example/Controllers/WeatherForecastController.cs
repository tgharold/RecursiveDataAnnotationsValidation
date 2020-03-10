using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptionsPatternMvc.Example.Settings;

namespace OptionsPatternMvc.Example.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly ExampleAppSettings _exampleAppSettings;

        public WeatherForecastController(
            ILogger<WeatherForecastController> logger,
            IOptions<ExampleAppSettings> exampleAppSettingsAccessor
            )
        {
            _logger = logger;
            _exampleAppSettings = exampleAppSettingsAccessor.Value;
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            Debug.WriteLine($"Settings.Name={_exampleAppSettings.Name}");
            
            var rng = new Random();
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = rng.Next(-20, 55),
                Summary = Summaries[rng.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }
}
