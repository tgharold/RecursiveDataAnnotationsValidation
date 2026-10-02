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
- A property that builds a new object on each read, such as `public Money Zero => new Money(0)`, could make the walk go on forever. So when an object's type overrides `Equals`, and the object equals an object of the same type, a base type or a derived type on its own path from the root, the validator checks that object's own attributes but does not walk into its properties. An invalid object below it is not reached.
- For a type that does not override `Equals`, mark such a property with `[SkipRecursiveValidation]`. Otherwise the walk overflows the stack, which ends the process.
- Two records that reference each other can also overflow the stack. A record's generated `Equals` compares properties in declaration order, so it follows the reference forever if it reaches it before a property that differs. This happens when the records have equal values, or when the reference is declared first. On .NET Framework it always happens, because the framework's `Validator` calls the record's generated `GetHashCode`, which follows the reference too. Marking the reference with `[SkipRecursiveValidation]` avoids the walk, but not the `GetHashCode` call on .NET Framework. There, override `GetHashCode` so it does not include the reference.

### Collections

- The items of a collection that a property holds are validated. The error names the property and the index of the item: `Items[1].Name`.
- An item that is itself a collection is validated too, at each level: `Matrix[0][2].Name`. This holds for lists, arrays, sets, dictionaries and your own collection types. A dictionary is enumerated as `KeyValuePair` items, so its values are reported as `Map[0].Value.Name`.
- An item that is a collection is validated as an object first, so its own attributes and `IValidatableObject.Validate` run, then its items.
- A collection of simple values, such as `List<int>` or `string[]`, is not enumerated, because it cannot hold an invalid object.
- A collection that you pass to the validator as the root object is not enumerated. Wrap it in an object with a property.
- A collection that is a struct, such as `ImmutableArray<T>`, is validated when it is an item of another collection, but not when a property holds it. The validator only reads properties of reference types.
- The validator does not catch exceptions. If a collection throws when it is enumerated, the exception reaches your code. A default `ImmutableArray<T>` item is skipped, because it holds nothing.

### Framework types that are not walked

The validator does not walk the properties that these framework types declare: `Type` and other `MemberInfo` types, `Assembly`, `Module`, delegates, `Uri`, `FileSystemInfo`, which covers `DirectoryInfo` and `FileInfo`, `Thread`, and `Process`. Reading those properties throws or never ends, for example `Uri.Segments` on a relative `Uri`, and they carry no validation attributes.

An object of one of these types is still validated. Attributes on the property that holds it, such as `[Required]` on a `Uri` property, still run. A subclass of your own, such as a class derived from `Uri`, is validated too, and the properties it adds are walked.

Other framework types are walked, so your objects inside them are validated. Examples are tuples, `KeyValuePair` items of a dictionary, and collections.

## Build Status

![.NET Core](https://github.com/tgharold/RecursiveDataAnnotationsValidation/workflows/.NET%20Core/badge.svg)

## Nuget Page

https://www.nuget.org/packages/RecursiveDataAnnotationsValidation/

## History and attribution

This package grew out of a need to recursively validate POCOs used for the [.NET Core options pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options). It is based on the [DataAnnotationsValidatorRecursive](https://github.com/reustmd/DataAnnotationsValidatorRecursive) project by [Mike Reust](https://github.com/reustmd). After a lot of [experimentation](https://github.com/tgharold/DotNetCore-ConfigurationOptionsValidationExamples), I forked that project. My goals at the time were to make minor improvements, port it to .NET Standard, and experiment with [GitHub Actions](https://docs.github.com/en/actions). The two projects have since evolved independently.

Mike Reust's original copyright is retained in the [LICENSE](https://github.com/tgharold/RecursiveDataAnnotationsValidation/blob/master/LICENSE).
