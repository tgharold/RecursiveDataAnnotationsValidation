using System;

namespace RecursiveDataAnnotationsValidation.Attributes
{
    /// <summary>Used to decorate child object properties where you do not want the
    /// child object to be validated by the recursive validation.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class SkipRecursiveValidationAttribute : Attribute
    {
        
    }
}