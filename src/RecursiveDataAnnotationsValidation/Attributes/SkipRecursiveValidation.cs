using System;

namespace RecursiveDataAnnotationsValidation.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Enum)]
    public class SkipRecursiveValidationAttribute : Attribute
    {
        
    }
}