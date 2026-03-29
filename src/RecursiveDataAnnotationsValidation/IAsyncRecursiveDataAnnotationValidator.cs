using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace RecursiveDataAnnotationsValidation
{
    /// <summary>Async interface for the RecursiveDataAnnotationValidator. Useful if you need
    /// to swap in a different approach, or to mock the methods.</summary>
    public interface IAsyncRecursiveDataAnnotationValidator
    {
        /// <summary>Runs async validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationContext">Validation context.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur.</param>
        /// <returns>Returns true if all validation passes.</returns>
        Task<bool> TryValidateObjectRecursiveAsync(
            object obj,
            ValidationContext validationContext,
            List<ValidationResult> validationResults
            );

        /// <summary>Runs async validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur.</param>
        /// <param name="validationContextItems">Validation context items.</param>
        /// <returns>Returns true if all validation passes.</returns>
        Task<bool> TryValidateObjectRecursiveAsync(
            object obj,
            List<ValidationResult> validationResults,
            IDictionary<object, object> validationContextItems = null
            );
    }
}