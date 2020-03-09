using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptionsPatternMvc.Example.Attributes;

namespace OptionsPatternMvc.Example.Extensions
{
    public static class IServiceCollectionExtensions
    {
        /// <summary>Bind a section of the appsettings.json to a POCO along with wiring up
        /// recursively-validated DataAnnotation validation.</summary>
        public static IServiceCollection AddValidatedSettings<T>(
            this IServiceCollection services,
            IConfiguration configuration
        ) where T : class
        {
            var sectionName = GetSettingsSectionName<T>();
            
            var configurationSection = configuration.GetSection(sectionName);
            
            services.AddOptions<T>()
                .Bind(configurationSection)
                .RecursivelyValidateDataAnnotations();
            
            return services;
        }
        
        /// <summary>Get the section name within appsettings.json.  If the SettingsSectionNameAttribute
        /// is present, use that as the section name in the JSON.  Otherwise fall back to the type name.
        /// </summary>
        private static string GetSettingsSectionName<T>() where T : class
        {
            return typeof(T).GetCustomAttribute<SettingsSectionNameAttribute>()?.SectionName
                   ?? typeof(T).Name;
        }
    }
}