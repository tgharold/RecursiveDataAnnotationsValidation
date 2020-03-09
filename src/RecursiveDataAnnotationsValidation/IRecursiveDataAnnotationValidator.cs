using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation
{
    public interface IRecursiveDataAnnotationValidator
    {
        bool TryValidateObjectRecursive<T>(
            T obj,
            ValidationContext validationContext,
            List<ValidationResult> validationResults
            ) where T : class;

        bool TryValidateObjectRecursive<T>(
            T obj,
            List<ValidationResult> validationResults,
            IDictionary<object, object> validationContextItems = null
            );
    }
}