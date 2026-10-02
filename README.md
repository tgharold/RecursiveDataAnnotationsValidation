# RecursiveDataAnnotationsValidation

Validates a whole object graph with [DataAnnotations](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations) attributes. `Validator.TryValidateObject` checks only the properties of the object you pass it. It does not validate the objects that those properties hold, or the items of a collection. This library walks the graph and validates each object it finds.

## Installation

### .NET CLI

    $ dotnet add package RecursiveDataAnnotationsValidation

### Package Manager

    PM> Install-Package RecursiveDataAnnotationsValidation

## Usage

Usage of the recursive validation is nearly identical to using the standard validator.

```csharp
var validator = new RecursiveDataAnnotationValidator();
var validationResults = new List<ValidationResult>();
var isValid = validator.TryValidateObjectRecursive(model, validationResults);
```

There are more examples in the [example](https://github.com/tgharold/RecursiveDataAnnotationsValidation/tree/master/examples) and [test](https://github.com/tgharold/RecursiveDataAnnotationsValidation/tree/master/test) projects.

### Member names

The member name of a nested error is the path from the root object: `Customer.Address.Zip`, `Lines[1].Quantity`. The message is the one that the nested object produced, so it names only its own property. An error of a whole nested object, such as one from a class-level attribute or from `IValidatableObject.Validate` with no member names, gets the path of the object as its member name: `Lines[1]`. An error of the root object keeps the member names it has, so a class-level error of the root has none.

### SkipRecursiveValidationAttribute

The [`[SkipRecursiveValidation]`](https://github.com/tgharold/RecursiveDataAnnotationsValidation/blob/master/src/RecursiveDataAnnotationsValidation/Attributes/SkipRecursiveValidation.cs) attribute can be used on properties where you do not want to recursively validate.  An example of this can be seen in [SkippedChildrenExample.cs](https://github.com/tgharold/RecursiveDataAnnotationsValidation/blob/master/test/RecursiveDataAnnotationsValidation.Tests/TestModels/SkippedChildrenExample.cs).

### Shared objects, cycles and computed properties

- Each object is validated once, even when several properties point to it. This also stops cycles, such as a child that points back to its parent. The error names the shortest path to the object (see "Order of results" below).
- A struct is the exception to the first rule. The validator cannot tell that two copies of a struct are the same value, so it reports an invalid struct once for each route that reaches it. For example, two properties that hold the same array of structs report each struct twice, so do two properties that return the same struct, such as a property and a computed copy of it, and so does a `List<object>` that holds an `ArraySegment<T>` of structs, because the segment returns its items by enumeration and through its `Array` property. The error is not lost, but the result list has a duplicate with a different member name. For a collection type of your own, mark the property that repeats the items with `[SkipRecursiveValidation]`.
- Objects are compared by reference. Two separate objects that are `Equals` to each other, such as records with the same values or entities with the same `Id`, are each validated.
- Only public instance properties are walked. A static property holds data of the type, not of your object, so it is not walked, as `Validator` ignores it too.
- A property that builds a new object on each read, such as `public Money Zero => new Money(0)`, could make the walk go on forever. So when an object's type overrides `Equals`, and the object equals an object of the same type, a base type or a derived type on its own path from the root, the validator checks that object's own attributes but does not walk into its properties from there. The check uses the shortest path to the object. A longer path that does not pass an equal object walks into it. An invalid object that every path reaches through an equal ancestor is not reached. This can happen when a shared object, such as a customer, is reached first through an equal ancestor.
- For a type that does not override `Equals`, mark such a property with `[SkipRecursiveValidation]`. Otherwise the walk goes on until it reaches the maximum depth (see below), and the validation fails.
- Two records that reference each other can also overflow the stack. A record's generated `Equals` compares properties in declaration order, so it follows the reference forever if it reaches it before a property that differs. This happens when the records have equal values, or when the reference is declared first. On .NET Framework it always happens, because the framework's `Validator` calls the record's generated `GetHashCode`, which follows the reference too. Marking the reference with `[SkipRecursiveValidation]` avoids the walk, but not the `GetHashCode` call on .NET Framework. There, override `GetHashCode` so it does not include the reference.

### Maximum depth

The validator walks at most 128 levels. The depth of an object is the number of segments in the shortest path to it: each property and each collection index is one level, and the root object is level 0. A link back to a parent, or a second path to a shared object, does not make an object deeper. In `Items[1].Name`, `Items` is level 1, `[1]` is level 2 and `Name` is level 3. System.Text.Json counts nearly the same way, with each object and each array as one level, and its default limit is 64. A JSON document within that limit stays within about 63 levels here.

An object at level 128 is validated. An object at level 129 is not validated, and the validation fails with one error at its path: `The object is nested more than 128 levels deep and was not validated.` The walk does not go deeper below that object. A graph that is too deep means that something has gone wrong, such as a property that builds a new object on each read, so the validator fails the validation instead of letting the graph pass. It does not throw. The limit does not help a property that builds two or more new objects on each read. The number of objects doubles at each level, so the walk runs until memory runs out, long before level 128. Mark such a property with `[SkipRecursiveValidation]`.

A tree that holds its children in a `List<T>` uses two levels for each tree level, so it can be 64 levels deep. The limit is fixed.

### Order of results

The validator walks the graph one level at a time, from the root outward. The results come in that order: the root object's own errors first, then the errors of the objects one level below it, then two levels below it, and so on. The errors of one object stay together, in the order that `Validator.TryValidateObject` returns them. Objects on the same level come in property order. When an item of a collection is itself a collection, its own items come before the objects that its properties hold. An item of a collection that a property holds is two levels below that object, one for the property and one for the index. So with the properties `First`, `Items` and `Last`, the error of `Last` comes before the error of `Items[0]`. Do not rely on the order of the list. Sort it if you need a fixed order.

An object that several paths reach is validated once, and its errors name the shortest path. If two paths are equally short, the one through the property that comes first wins. This matters most for a model with links back to its parents, such as the entities of an ORM. An order that is in `Store.Orders` is reported as `Orders[0].Code`, not as a long chain of links through its lines and products.

### Collections

- The items of a collection that a property holds are validated. The error names the property and the index of the item: `Items[1].Name`.
- An item that is itself a collection is validated too, at each level: `Matrix[0][2].Name`. This holds for lists, arrays, sets, dictionaries and your own collection types. A dictionary is enumerated as `KeyValuePair` items, so a value that is an object is reported as `Map[0].Value.Name`. A value that is a struct is walked when it has something to validate, as it is when a property holds it (see "Structs" below).
- An item that is a collection is validated as an object first, so its own attributes and `IValidatableObject.Validate` run, then its items.
- A collection of simple values, such as `List<int>` or `string[]`, is not enumerated, because it cannot hold an invalid object.
- A collection that you pass to the validator as the root object is validated as an object, then its items are validated. The error starts with the index of the item: `[1].Name`.
- A collection that is a struct, such as `ImmutableArray<T>`, is validated when a property holds it and when it is an item of another collection: `Lines[0].Sku`.
- The validator runs each collection it enumerates, so a lazy sequence, such as a LINQ query or an iterator, runs during validation. A sequence that never ends makes validation hang. The validator does not catch exceptions, so an exception that a collection throws when it is enumerated reaches your code. A struct collection that is its default value, such as an `ImmutableArray<T>` or an `ArraySegment<T>` that nobody set, is skipped when it is an item or when the property is declared as that struct, because it holds nothing and enumerating it throws. The validator compares the memory of the struct with its default. It does not call the `Equals` of your struct. A property declared as an interface or as `object` is always enumerated.

### Structs

- A struct that a property holds is walked when it has something to validate: a validation attribute on the struct or on one of its properties, `IValidatableObject`, or a property that holds an object, such as the `Value` of a `KeyValuePair<string, Child>`. The error names the path, as for a class: `Price.Amount`, `Pair.Value.Name`.
- A struct with nothing to validate, such as an `int`, a `Guid` or a `DateTime`, is skipped.
- A struct is validated even when it is its default value. A default `Money` with `[Range(1, 10)]` on its `Amount` fails. Only a struct collection that is its default value is skipped (see "Collections" above).
- A property of a struct that returns its own struct type, such as `DateTime.Date`, is not walked. Each read returns a new copy, so the walk would not end on its own.
- A value tuple such as `(Child, int)` keeps its items in fields, not in properties. `Validator` reads only properties, so the items of a value tuple are not walked. Use a class, a record or a `Tuple<Child, int>` instead.

### Framework types that are not walked

The validator does not walk the properties that these framework types declare: `Type` and other `MemberInfo` types, `Assembly`, `Module`, delegates, `Uri`, `FileSystemInfo`, which covers `DirectoryInfo` and `FileInfo`, `Thread`, `Process`, `GCHandle`, the SqlTypes such as `SqlString`, which implement `INullable`, and `ValueTask<T>`. Reading those properties throws, waits or never ends, for example `Uri.Segments` on a relative `Uri`, or `ValueTask<T>.Result` before the task has finished, and they carry no validation attributes.

An object of one of these types is still validated. Attributes on the property that holds it, such as `[Required]` on a `Uri` property, still run. A subclass of your own, such as a class derived from `Uri`, is validated too, and the properties it adds are walked.

Other framework types are walked, so your objects inside them are validated. Examples are the `Tuple` classes, such as `Tuple<Child, int>`, `KeyValuePair`, and collections.

## Build Status

![.NET Core](https://github.com/tgharold/RecursiveDataAnnotationsValidation/workflows/.NET%20Core/badge.svg)

## Nuget Page

https://www.nuget.org/packages/RecursiveDataAnnotationsValidation/

## History and attribution

This package grew out of a need to recursively validate POCOs used for the [.NET Core options pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options). It is based on the [DataAnnotationsValidatorRecursive](https://github.com/reustmd/DataAnnotationsValidatorRecursive) project by [Mike Reust](https://github.com/reustmd). After a lot of [experimentation](https://github.com/tgharold/DotNetCore-ConfigurationOptionsValidationExamples), I forked that project. My goals at the time were to make minor improvements, port it to .NET Standard, and experiment with [GitHub Actions](https://docs.github.com/en/actions). The two projects have since evolved independently.

Mike Reust's original copyright is retained in the [LICENSE](https://github.com/tgharold/RecursiveDataAnnotationsValidation/blob/master/LICENSE).
