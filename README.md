# RecursiveDataAnnotationsValidation

Allows recursive validation of sub-objects in a class when using [DataAnnotations validation](https://docs.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-3.1) (also known as Attribute Validation).  The current version of .NET Core's attribute validation does not handle objects within objects (or collections of objects).  Therefore it is necessary to add some glue code to recurse through the object graph.

## Installation

### .NET Core

    $ dotnet add package RecursiveDataAnnotationsValidation

### Package Manager

    PM> Install-Package RecursiveDataAnnotationsValidation

## Usage

Usage of the recursive validation is nearly identical to using the standard validator.

    var validator = new RecursiveDataAnnotationValidator();
    var validationResults = new List<ValidationResult>();
    var result = validator.TryValidateObjectRecursive(sut, validationResults);
    
There are more examples in the [example](https://github.com/tgharold/RecursiveDataAnnotationsValidation/tree/master/examples) and [test](https://github.com/tgharold/RecursiveDataAnnotationsValidation/tree/master/test) projects.

### SkipRecursiveValidationAttribute

The [`[SkipRecursiveValidation]`](https://github.com/tgharold/RecursiveDataAnnotationsValidation/blob/master/src/RecursiveDataAnnotationsValidation/Attributes/SkipRecursiveValidation.cs) attribute can be used on properties where you do not want to recursively validate.  An example of this can be seen in [SkippedChildrenExample.cs](https://github.com/tgharold/RecursiveDataAnnotationsValidation/blob/master/test/RecursiveDataAnnotationsValidation.Tests/TestModels/SkippedChildrenExample.cs).

## Build Status

![.NET Core](https://github.com/tgharold/RecursiveDataAnnotationsValidation/workflows/.NET%20Core/badge.svg)

## Nuget Page

https://www.nuget.org/packages/RecursiveDataAnnotationsValidation/

## Legacy

This package grew out of a need to recursively validate POCOs used for the [.NET Core options pattern](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-3.1) and is based on work by [Mike Reust](https://github.com/reustmd) and his [DataAnnotationsValidatorRecursive](https://github.com/reustmd/DataAnnotationsValidatorRecursive) project.  After doing a lot of [experimentation](https://github.com/tgharold/DotNetCore-ConfigurationOptionsValidationExamples), I went ahead and forked the project in order to make minor improvements, port it to .NET Standard, and experiment with using [Github Actions](https://docs.github.com/en/actions).
