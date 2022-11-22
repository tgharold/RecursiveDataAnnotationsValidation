using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace RecursiveDataAnnotationsValidation.Tests.Attributes;

/// <summary>Examine each element in an IEnumerable of string and return false (not valid) if
/// any of the string values are null/whitespace.  It makes no attempt to return the
/// index number of the invalid entry; it only reports back a generic error for
/// the IEnumerable of string property.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property)]
public class EnumerableStringNotNullOrWhitespaceAttribute : ValidationAttribute
{
    public EnumerableStringNotNullOrWhitespaceAttribute()
    {
        ErrorMessage = "Found elements that are null or whitespace.";
    }
    
    public override bool IsValid(object value)
    {
        if (value is not IEnumerable<string> enumerableStrings) return true;

        return !enumerableStrings.Any(x => string.IsNullOrWhiteSpace(x));
    }
}