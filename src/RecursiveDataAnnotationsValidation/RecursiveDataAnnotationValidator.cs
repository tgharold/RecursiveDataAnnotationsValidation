using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Attributes;
using RecursiveDataAnnotationsValidation.Extensions;

namespace RecursiveDataAnnotationsValidation
{
    public class RecursiveDataAnnotationValidator : IRecursiveDataAnnotationValidator
    {
        public bool TryValidateObjectRecursive<T>(
            T obj, 
            ValidationContext validationContext, 
            List<ValidationResult> validationResults
            ) where T : class
        {
            return TryValidateObjectRecursive(
                obj, 
                validationResults,
                validationContext.Items
            );
        }

        private bool TryValidateObject(
            object obj, 
            ICollection<ValidationResult> validationResults, 
            IDictionary<object, object> validationContextItems = null
            )
        {
            return Validator.TryValidateObject(
                obj, 
                new ValidationContext(
                    obj, 
                    null,
                    validationContextItems
                    ), 
                validationResults, 
                true
                );
        }

        public bool TryValidateObjectRecursive<T>(
            T obj, 
            List<ValidationResult> validationResults, 
            IDictionary<object, object> validationContextItems = null
            )
        {
            return TryValidateObjectRecursive(
                obj, 
                validationResults, 
                new HashSet<object>(), 
                validationContextItems
                );
        }

        private bool TryValidateObjectRecursive<T>(
            T obj, 
            ICollection<ValidationResult> validationResults, 
            ISet<object> validatedObjects, 
            IDictionary<object, object> validationContextItems = null
            )
        {
            //short-circuit to avoid infinite loops on cyclical object graphs
            if (validatedObjects.Contains(obj))
            {
                return true;
            }

            validatedObjects.Add(obj);
            var result = TryValidateObject(obj, validationResults, validationContextItems);

            var properties = obj.GetType().GetProperties().Where(prop => prop.CanRead
                && !prop.GetCustomAttributes(typeof(SkipRecursiveValidationAttribute), false).Any()
                && prop.GetIndexParameters().Length == 0).ToList();

            foreach (var property in properties)
            {
                if (property.PropertyType == typeof(string) || property.PropertyType.IsValueType) continue;

                var value = obj.GetPropertyValue(property.Name);

                List<ValidationResult> nestedResults;
                switch (value)
                {
                    case null:
                        continue;
                    
                    case IEnumerable asEnumerable:
                        foreach (var enumObj in asEnumerable)
                        {
                            if (enumObj == null) continue;
                            nestedResults = new List<ValidationResult>();
                            if (!TryValidateObjectRecursive(
                                enumObj, 
                                nestedResults, 
                                validatedObjects, 
                                validationContextItems
                                ))
                            {
                                result = false;
                                foreach (var validationResult in nestedResults)
                                {
                                    var property1 = property;
                                    validationResults.Add(
                                    new ValidationResult(
                                        validationResult.ErrorMessage, 
                                        validationResult.MemberNames.Select(x => property1.Name + '.' + x)
                                        ));
                            }
                            }
                        }
                        break;
                    
                    default:
                        nestedResults = new List<ValidationResult>();
                        if (!TryValidateObjectRecursive(
                            value, 
                            nestedResults, 
                            validatedObjects, 
                            validationContextItems
                            ))
                        {
                            result = false;
                            foreach (var validationResult in nestedResults)
                            {
                                var property1 = property;
                                validationResults.Add(
                                new ValidationResult(
                                    validationResult.ErrorMessage, 
                                    validationResult.MemberNames.Select(x => property1.Name + '.' + x)
                                    ));
                            }
                        }
                        break;
                }
            }

            return result;
        }
    }
}