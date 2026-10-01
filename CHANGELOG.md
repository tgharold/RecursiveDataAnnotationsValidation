# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses [Semantic Versioning](https://semver.org/).

Breaking changes are marked **BREAKING**.

## Unreleased

### Changed

- Collection properties are no longer enumerated when their declared item type has nothing to validate: a value type or sealed class with no validation attribute on the type or its properties, no `IValidatableObject` implementation, and no property the validator walks into. Examples are primitives, enums, `string`, `DateTime`, `Guid` and `DateOnly`, and `Nullable` and `KeyValuePair` items of these types. Attributes added with `TypeDescriptor.AddAttributes` count, so validation results do not change. Large payloads such as a `byte[]` or a `Dictionary<string, string>` validate much faster.
- Because these collections are no longer enumerated, their enumerators no longer run during validation. A lazy sequence such as a LINQ query or an `IQueryable<int>` is not executed, so an exception it would throw while enumerating no longer surfaces.
- Release workflow: run the tests on .NET 8, .NET 10 and .NET Framework 4.8.1, on Linux and Windows, before publishing. The package is now built with the .NET 10 SDK.

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
