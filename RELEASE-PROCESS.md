# Release Process

## NuGet Trusted Publishing

The release workflow publishes to nuget.org with Trusted Publishing. No API key is stored in GitHub.

1. Sign in to nuget.org and open your account menu, Trusted Publishing.
2. Add a policy with the repository owner, the repository name, and the workflow file `create-release-asset-on-git-tag.yml`.
3. Set the environment to `nuget`.
4. Set the repository variable `NUGET_USER` to your nuget.org profile name: `gh variable set NUGET_USER --body "<nuget-profile-name>"`.

## Create Tag

1. Create an [annotated git tag](https://git-scm.com/book/en/v2/Git-Basics-Tagging) on the commit for the release. Such as `$ git tag -a v1.1.0 -m "Release v1.1.0"`
2. Push the tag to the repository. The tag must be on a commit that is on `master`, and the `v*` tag ruleset allows only repository admins to create it.
3. The release workflow starts and waits for approval of the `nuget` environment. Approve the run in GitHub Actions.
4. After approval, the build, GitHub release, and nuget.org publish run automatically.
