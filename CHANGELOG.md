# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/).

Breaking changes are marked **BREAKING**.

## Unreleased

### Changed

- Collection properties are no longer enumerated when their declared item type has nothing to validate: a value type or sealed class with no validation attribute on the type or its properties, no `IValidatableObject` implementation, and no property the validator walks into. Examples are primitives, enums, `string`, `DateTime`, `Guid` and `DateOnly`, and `Nullable` and `KeyValuePair` items of these types. Attributes added with `TypeDescriptor.AddAttributes` count, so validation results do not change. Large payloads such as a `byte[]` or a `Dictionary<string, string>` validate much faster.
- Items in a collection declared with `object` items, such as `object[]`, `List<object>` or `Dictionary<string, object>`, are skipped when their runtime type has nothing to validate, by the same rule.
- Because these collections are no longer enumerated, their enumerators no longer run during validation. A lazy sequence such as a LINQ query or an `IQueryable<int>` is not executed, so an exception it would throw while enumerating no longer surfaces.
- Release workflow: run the tests on .NET 8, .NET 10 and .NET Framework 4.8.1, on Linux and Windows, before publishing.
- The recursive validator no longer calls `GetHashCode` on your objects. It calls `Equals` only to compare an object with its ancestors of the same type, a base type or a derived type. On .NET Framework, the framework's `Validator` still calls `GetHashCode`, through `TypeDescriptor`.
- README: describe how shared objects, cycles and computed properties are handled.
- The package README is now the repository README, so it also includes the build status and the history and attribution section. The "Legacy" section is renamed "History and attribution".

### Fixed

- Objects that compare equal but are separate instances are now each validated. Before, only the first was validated, so an invalid object passed when it was `Equals` to one already seen, for example an entity whose `Equals` compares only an `Id`. Models that passed only because of this may now fail.
- An object that is `Equals` to one of its own ancestors of a related type, such as a sub-folder with its parent's `Id`, now has its own attributes validated. The validator still does not walk into it, so a property that builds a new, equal object on each read, such as `Money Zero => new Money(0)`, still stops the walk as before.
- On .NET 8 and later, two records that reference each other no longer always overflow the stack. They still do when the generated `Equals` reaches the reference before a property that differs, because it compares properties in declaration order. On .NET Framework they still always overflow, inside the framework's `Validator`.
- A null `validationResults` list no longer throws `NullReferenceException` when a nested object or collection item fails. Like `Validator.TryValidateObject`, the validator now returns false.
- The service provider of the `ValidationContext` you pass now reaches every object the validator visits, so `ValidationContext.GetService` in an attribute or `IValidatableObject` returns your service instead of null. The overloads that take only context items still have no service provider.
- A property or collection item of type `Type`, `MethodInfo`, `Assembly`, `Module`, a delegate, or a relative `Uri` no longer makes validation throw, and a `DirectoryInfo` or `FileInfo` no longer overflows the stack. Objects of these framework types, and of types derived from them, are no longer validated or walked. They carry no validation attributes.

## 2.2.4 - 2026-10-01

### Changed

- A null `validationContext` now throws `ArgumentNullException` instead of `NullReferenceException`. A null `obj` still throws `ArgumentNullException`, but its `ParamName` is now `obj` instead of `instance`.
- Add `CHANGELOG.md`. It replaces `BREAKING-CHANGES.md`.
- Release workflow: generate GitHub release notes. Mark only tags with a suffix, such as `v1.5.0-alpha.1`, as prereleases, so a stable release can be Latest.

### Fixed

- Validation no longer throws `AmbiguousMatchException` when a derived class hides a base property with `new` and a different type. Both properties are now validated.

## 2.2.3 - 2026-09-30

No library changes. The NuGet package contents are the same as v2.2.0.

### Changed

- Release workflow: publish to nuget.org with Trusted Publishing instead of a stored API key.
- Release workflow: require release tags to be on `master`.
- Release workflow: create the GitHub release as a draft and publish it last.

## 2.2.2 - 2026-09-30

No library changes. This was a test release while hardening the release workflow.

## 2.2.1 - 2026-09-30

No library changes.

### Changed

- Harden the GitHub Actions workflows.
- Remove `dependabot.yml`.

## 2.2.0 - 2026-03-29

### Added

- Async validation. `IAsyncRecursiveDataAnnotationValidator` defines `TryValidateObjectRecursiveAsync`, and `RecursiveDataAnnotationValidator` now implements it. The async methods run the existing synchronous validation on a thread-pool thread with `Task.Run`.

### Fixed

- The README was missing from the NuGet package.

## 2.1.1 - 2026-03-28

### Changed

- Upgrade test and benchmark dependencies, including BenchmarkDotNet 0.15.8. No library changes.

## 2.1.0 - 2026-03-28

The library still targets .NET Standard 2.0. The .NET 8 change applies to the build, tests, benchmarks and example project only.

### Added

- The NuGet package now includes a README and a symbols package (`.snupkg`).

### Changed

- Build, test, benchmark and example projects now target .NET 8.
- Rework the GitHub Actions release workflow to build and attach the `.nupkg` and `.snupkg` files.
- Add more tests for nested enumerables.
- Add `CLAUDE.md` for AI coding assistants.

## 2.0.0 - 2022-11-18

### Changed

- **BREAKING:** Member names for items inside an `IEnumerable` property now include the item index in square brackets (issue [#24](https://github.com/tgharold/RecursiveDataAnnotationsValidation/issues/24)).

  - Old: `Items.SimpleA.BoolC`
  - New: `Items[1].SimpleA.BoolC`

  Update any code that parses or matches these member name strings. Code that only checks the result of `TryValidateObjectRecursive` is not affected.

### Added

- A benchmark project.
- More tests for lists and other collections.

## 1.1.1 - 2022-06-23

### Changed

- Update test project packages. No library changes.

## 1.1.0 - 2022-01-10

### Added

- XML documentation for the public types and methods, shipped with the package.

### Changed

- Warnings are treated as errors when building the library.
- Update test project dependencies.

## 1.0.0 - 2020-03-11

First public release.

## 0.9.1 - 2020-03-11

First automated push to nuget.org.

## 0.9.0 - 2020-03-10

First version ready for public use. Includes:

- `RecursiveDataAnnotationValidator` for validating an object graph recursively.
- Protection against infinite loops in cyclical object references.
- Recursive validation of `IEnumerable` collections.
- `[SkipRecursiveValidation]` to exclude a property from recursive validation.
