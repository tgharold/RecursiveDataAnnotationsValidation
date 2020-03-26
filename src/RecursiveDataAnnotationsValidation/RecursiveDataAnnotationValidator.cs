using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RecursiveDataAnnotationsValidation.Attributes;
using RecursiveDataAnnotationsValidation.Extensions;

namespace RecursiveDataAnnotationsValidation
{
    /// <summary>Recursive validator for DataAnnotation attribute-based validation.</summary>
    public class RecursiveDataAnnotationValidator : IRecursiveDataAnnotationValidator
    {
        /// <summary>Runs validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationContext">Validation context.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur.</param>
        /// <returns>Returns true if all validation passes.</returns>
        public bool TryValidateObjectRecursive(
            object obj,  // see Note 1 
            ValidationContext validationContext, 
            List<ValidationResult> validationResults
            )
        {
            return TryValidateObjectRecursive(
                obj,
                validationResults,
                validationContext.Items
            );
        }

        /// <summary>Runs validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur.</param>
        /// <param name="validationContextItems">Validation context items.</param>
        /// <returns>Returns true if all validation passes.</returns>
        public bool TryValidateObjectRecursive(
            object obj, 
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

        private bool TryValidateObjectRecursive(
            object obj, 
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
                        foreach (var item in asEnumerable)
                        {
                            //NOTE: This does not tell you which item in the IEnumerable<T> failed
                            //Possibly, should have a separate case for Array/Dictionary
                            
                            if (item == null) continue;
                            nestedResults = new List<ValidationResult>();
                            if (!TryValidateObjectRecursive(
                                item, 
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
        
        /* Note 1:
         *
         * Background information of why we don't use ValidationContext.ObjectInstance here, even though it is tempting.
         *
         * https://jeffhandley.com/2009-10-17/validator
         *
         * It’s important to note that for cross-field validation, relying on the ObjectInstance comes with a caveat.
         * It’s possible that the end user has entered a value for a property that could not be set—for instance
         * specifying “ABC” for a numeric field.  In cases like that, asking the instance for that numeric property
         * will of course not give you the “ABC” value that the user has entered, thus the object’s other properties
         * are in an indeterminate state.  But even so, we’ve found that it’s extremely valuable to provide this object
         * instance to the validation attributes.
         *
         * See also:
         * 
         * https://github.com/dotnet/corefx/blob/8b04d0a18a49448ff7c8ee63239cd6d2a2be7e14/src/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/ValidationContext.cs
         * 
         */
    }
}