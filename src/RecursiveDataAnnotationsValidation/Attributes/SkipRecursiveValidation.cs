using System;

namespace RecursiveDataAnnotationsValidation.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class SkipRecursiveValidationAttribute : Attribute
    {
        
    }
}