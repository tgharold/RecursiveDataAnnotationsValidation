using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Extensions;

namespace RecursiveDataAnnotationsValidation
{
    /// <summary>Recursive validator for DataAnnotation attribute-based validation.</summary>
    public class RecursiveDataAnnotationValidator : IRecursiveDataAnnotationValidator, IAsyncRecursiveDataAnnotationValidator
    {
        /// <summary>Runs validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationContext">Validation context.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur.</param>
        /// <returns>Returns true if all validation passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="validationContext"/> is null.</exception>
        public bool TryValidateObjectRecursive(
            object obj,  // see Note 1 
            ValidationContext validationContext, 
            List<ValidationResult> validationResults
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (validationContext == null) throw new ArgumentNullException(nameof(validationContext));

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
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
        public bool TryValidateObjectRecursive(
            object obj,
            List<ValidationResult> validationResults,
            IDictionary<object, object> validationContextItems = null
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            return TryValidateObjectRecursive(
                obj,
                validationResults,
                new HashSet<object>(ObjectReferenceComparer.Instance),
                new List<object>(),
                validationContextItems
                );
        }

        /// <summary>Runs async validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationContext">Validation context.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur.</param>
        /// <returns>Returns true if all validation passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> or <paramref name="validationContext"/> is null.</exception>
        public async Task<bool> TryValidateObjectRecursiveAsync(
            object obj,
            ValidationContext validationContext,
            List<ValidationResult> validationResults
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (validationContext == null) throw new ArgumentNullException(nameof(validationContext));

            return await Task.Run(() => TryValidateObjectRecursive(
                obj,
                validationResults,
                validationContext.Items
            ));
        }

        /// <summary>Runs async validation on an object.</summary>
        /// <param name="obj">The object being validated.</param>
        /// <param name="validationResults">A collection that will be populated if validation errors occur.</param>
        /// <param name="validationContextItems">Validation context items.</param>
        /// <returns>Returns true if all validation passes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
        public async Task<bool> TryValidateObjectRecursiveAsync(
            object obj,
            List<ValidationResult> validationResults,
            IDictionary<object, object> validationContextItems = null
            )
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            return await Task.Run(() => TryValidateObjectRecursive(
                obj,
                validationResults,
                new HashSet<object>(ObjectReferenceComparer.Instance),
                new List<object>(),
                validationContextItems
            ));
        }

        /// <summary>
        /// Validates the specified object and adds any validation results to the provided collection.
        /// </summary>
        /// <param name="obj">The object to validate.</param>
        /// <param name="validationResults">A collection to receive any validation errors.</param>
        /// <param name="validationContextItems">Optional context items for the validation context.</param>
        /// <returns>True if the object is valid; otherwise, false.</returns>
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

        //validatedObjects holds every object visited so far, compared by reference.
        //recordPath holds the records on the path from the root to this object.
        private bool TryValidateObjectRecursive(
            object obj, 
            ICollection<ValidationResult> validationResults, 
            ISet<object> validatedObjects, 
            List<object> recordPath,
            IDictionary<object, object> validationContextItems = null
            )
        {
            var type = obj.GetType();

            //an object of a leaf type can never produce a result, such as a boxed int in an object[] (see IsLeafType)
            if (type.IsLeafType())
            {
                return true;
            }

            //short-circuit to avoid infinite loops on cyclical object graphs
            if (validatedObjects.Contains(obj))
            {
                return true;
            }

            //a record property can return a new, equal record on each read, such as
            //`Point Origin => new Point(0, 0)`, so references alone never repeat. Stop at a record
            //that Equals one of its own ancestors. Classes are left out: a class Equals keyed on
            //an Id would skip a child that has its parent's Id.
            var isRecord = type.IsRecord();
            if (isRecord && recordPath.Any(ancestor => ancestor.GetType() == type && obj.Equals(ancestor)))
            {
                return true;
            }

            validatedObjects.Add(obj);
            if (isRecord) recordPath.Add(obj);

            var result = TryValidateObject(obj, validationResults, validationContextItems);

            var properties = type.GetProperties().Where(prop => prop.IsWalked()).ToList();

            foreach (var property in properties)
            {
                var value = property.GetValue(obj, null);

                List<ValidationResult> nestedResults;
                switch (value)
                {
                    case null:
                        continue;

                    //items of a leaf type can never produce a result, so don't enumerate them (see IsLeafType)
                    case IEnumerable _ when value.GetType().IsCollectionOfLeafType():
                        continue;

                    case IEnumerable asEnumerable:
                        var arrayIndex = -1;
                        foreach (var item in asEnumerable)
                        {
                            arrayIndex++;

                            //NOTE: Possibly should have a separate case for Dictionary which reports on the key
                            
                            if (item == null) continue;
                            nestedResults = new List<ValidationResult>();
                            if (!TryValidateObjectRecursive(
                                item, 
                                nestedResults, 
                                validatedObjects, 
                                recordPath,
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
                                        validationResult.MemberNames
                                            .Select(x => $"{property1.Name}[{arrayIndex}].{x}")
                                            .ToList()
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
                            recordPath,
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

            if (isRecord) recordPath.RemoveAt(recordPath.Count - 1);

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