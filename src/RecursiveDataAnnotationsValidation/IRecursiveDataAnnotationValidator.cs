using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RecursiveDataAnnotationsValidation
{
    public interface IRecursiveDataAnnotationValidator
    {
        bool TryValidateObjectRecursive(
            object obj,
            ValidationContext validationContext,
            List<ValidationResult> validationResults
            );

        bool TryValidateObjectRecursive(
            object obj,
            List<ValidationResult> validationResults,
            IDictionary<object, object> validationContextItems = null
            );
    }
}