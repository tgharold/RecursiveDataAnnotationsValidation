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
- A struct is the exception to the first rule. The validator cannot tell that two copies of a struct are the same value, so it reports an invalid struct once for each route that reaches it. For example, two properties that hold the same array of structs report each struct twice, and so does a `List<object>` that holds an `ArraySegment<T>` of structs, because the segment returns its items by enumeration and through its `Array` property. The error is not lost, but the result list has a duplicate with a different member name. For a collection type of your own, mark the property that repeats the items with `[SkipRecursiveValidation]`.
- Objects are compared by reference. Two separate objects that are `Equals` to each other, such as records with the same values or entities with the same `Id`, are each validated.
- Public static properties are walked, as well as instance properties.
- A property that builds a new object on each read, such as `public Money Zero => new Money(0)`, could make the walk go on forever. So when an object's type overrides `Equals`, and the object equals an object of the same type, a base type or a derived type on its own path from the root, the validator checks that object's own attributes but does not walk into its properties. An invalid object below it is not reached.
- For a type that does not override `Equals`, mark such a property with `[SkipRecursiveValidation]`. Otherwise the walk goes on until it reaches the maximum depth (see below), and the validation fails.
- Two records that reference each other can also overflow the stack. A record's generated `Equals` compares properties in declaration order, so it follows the reference forever if it reaches it before a property that differs. This happens when the records have equal values, or when the reference is declared first. On .NET Framework it always happens, because the framework's `Validator` calls the record's generated `GetHashCode`, which follows the reference too. Marking the reference with `[SkipRecursiveValidation]` avoids the walk, but not the `GetHashCode` call on .NET Framework. There, override `GetHashCode` so it does not include the reference.

### Maximum depth

The validator walks at most 128 levels. The depth of an object is the number of segments in its path: each property and each collection index is one level, and the root object is level 0. In `Items[1].Name`, `Items` is level 1, `[1]` is level 2 and `Name` is level 3. System.Text.Json counts nearly the same way, with each object and each array as one level, and its default limit is 64. A JSON document within that limit stays within about 63 levels here.

An object at level 128 is validated. An object at level 129 is not validated, and the validation fails with one error at its path: `The object is nested more than 128 levels deep and was not validated.` The walk does not go deeper below that object. A graph that is too deep means that something has gone wrong, such as a property that builds a new object on each read, so the validator fails the validation instead of letting the graph pass. It does not throw.

A tree that holds its children in a `List<T>` uses two levels for each tree level, so it can be 64 levels deep. The limit is fixed.

### Collections

- The items of a collection that a property holds are validated. The error names the property and the index of the item: `Items[1].Name`.
- An item that is itself a collection is validated too, at each level: `Matrix[0][2].Name`. This holds for lists, arrays, sets, dictionaries and your own collection types. A dictionary is enumerated as `KeyValuePair` items, so a value that is an object is reported as `Map[0].Value.Name`. A value that is a struct is not walked, because the validator reads only properties of reference types.
- An item that is a collection is validated as an object first, so its own attributes and `IValidatableObject.Validate` run, then its items.
- A collection of simple values, such as `List<int>` or `string[]`, is not enumerated, because it cannot hold an invalid object.
- A collection that you pass to the validator as the root object is not enumerated. Wrap it in an object with a property.
- A collection that is a struct, such as `ImmutableArray<T>`, is validated when it is an item of another collection, but not when a property holds it. The validator only reads properties of reference types.
- The validator runs each collection it enumerates, so a lazy sequence, such as a LINQ query or an iterator, runs during validation. A sequence that never ends makes validation hang. The validator does not catch exceptions, so an exception that a collection throws when it is enumerated reaches your code. A struct item that equals its default value, such as an `ImmutableArray<T>` or an `ArraySegment<T>` that nobody set, is skipped, because it holds nothing and enumerating it throws.

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
