# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET Standard library for recursive DataAnnotations validation, extending the built-in validation capabilities to handle nested objects and collections within objects.

The core functionality is provided by `RecursiveDataAnnotationValidator` which:
- Validates an object graph recursively, traversing nested properties
- Prevents infinite loops in cyclical object references
- Supports `IEnumerable` collections for recursive validation
- Allows skipping specific properties with `[SkipRecursiveValidation]` attribute

## Project Structure

The codebase has a clear separation between the main library and tests:

1. **src/RecursiveDataAnnotationsValidation/** - The core library project with:
   - Main validator implementation (`RecursiveDataAnnotationValidator.cs`)
   - Skip attribute for excluding properties from validation (`SkipRecursiveValidation.cs`)
   - Extension methods and interfaces

2. **test/RecursiveDataAnnotationsValidation.Tests/** - Test project with:
   - Tests for various recursive validation scenarios
   - Example test models (RecursionExample, EnumerableExample, etc.)

3. **examples/** - Example project showing usage patterns

## Commands for Development

### Build
```bash
dotnet build
```

### Run Tests
```bash
dotnet test
```

### Run Single Test
```bash
dotnet test test/RecursiveDataAnnotationsValidation.Tests/RecursiveDataAnnotationsValidation.Tests.csproj --filter "RecursionExampleTests.Passes_all_validation"
```

### Run Tests with Coverage
```bash
dotnet test test/RecursiveDataAnnotationsValidation.Tests/RecursiveDataAnnotationsValidation.Tests.csproj --collect:"XPlat Code Coverage"
```

## Key Files to Understand

- `RecursiveDataAnnotationValidator.cs` - Main implementation that handles recursive object validation
- `SkipRecursiveValidationAttribute.cs` - Attribute for excluding properties from recursive validation
- Test models in `test/RecursiveDataAnnotationsValidation.Tests/TestModels/` show various usage patterns

## Target Frameworks

- Main library targets `.NET Standard 2.0`
- Test project targets `net8.0` and `net10.0`. Add `-p:IncludeNetFramework=true` to also target `net481`, which runs only on Windows. CI runs it there.

## Development Notes

The recursive validator handles:
1. Cyclical object references to prevent infinite loops
2. Collections (IEnumerable) with recursive validation of items
3. Null value handling for nested objects and collections
4. Proper error message formatting that includes property paths

The validator uses reflection to examine object properties and recursively validate nested objects.

## Changelog

`CHANGELOG.md` follows the Keep a Changelog format. When you change the library's public API, validation behavior or NuGet package contents, add an entry under `Unreleased` in the same change. Use the headings Added, Changed, Deprecated, Removed, Fixed and Security.

Mark breaking changes with **BREAKING** and describe what callers must change. Breaking changes include changed member name formats, changed error messages that callers might parse, and removed or renamed public members. Keep entries short. Build, test and CI-only changes need an entry only when they affect what ships.

See `RELEASE-PROCESS.md` for how the changelog fits into a release.