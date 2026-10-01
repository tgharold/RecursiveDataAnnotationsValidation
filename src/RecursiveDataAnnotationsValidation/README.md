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

### Shared objects, cycles and computed properties

- Each object is validated once, even when several properties point to it. This also stops cycles, such as a child that points back to its parent.
- Objects are compared by reference. Two separate objects that are `Equals` to each other, such as records with the same values or entities with the same `Id`, are each validated.
- Public static properties are walked, as well as instance properties.
- A property that builds a new object on each read, such as `public Money Zero => new Money(0)`, could make the walk go on forever. So when an object's type overrides `Equals`, and the object equals an object on its own path from the root, the validator checks that object's own attributes but does not walk into its properties. An invalid object below it is not reached.
- For a type that does not override `Equals`, mark such a property with `[SkipRecursiveValidation]`. Otherwise the walk overflows the stack, which ends the process.
- Two records that reference each other can also overflow the stack. A record's generated `Equals` compares properties in declaration order, so it follows the reference forever if it reaches it before a property that differs. This happens when the records have equal values, or when the reference is declared first. To avoid it, mark the reference with `[SkipRecursiveValidation]`.
