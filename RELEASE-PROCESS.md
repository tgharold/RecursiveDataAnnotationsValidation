# Release Process

## Nuget API key

1. Check expirations of API keys at https://www.nuget.org/account/apikeys
2. Regenerate key if necessary.
3. GitHub, repository settings, Secrets...
4. Update the Nuget API key with the new value.

## Create Tag

1. Create an [annotated git tag](https://git-scm.com/book/en/v2/Git-Basics-Tagging) on the commit for the release. Such as `$ git tag -a v1.1.0 -m "Release v1.1.0"`
2. Push the tag to the repository.
3. An automatic build and release will occur.

