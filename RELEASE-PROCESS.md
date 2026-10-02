# Release Process

## NuGet Trusted Publishing

The release workflow publishes to nuget.org with Trusted Publishing. No API key is stored in GitHub.

1. Sign in to nuget.org and open your account menu, Trusted Publishing.
2. Add a policy with the repository owner, the repository name, and the workflow file `create-release-asset-on-git-tag.yml`.
3. Set the environment to `nuget`.
4. Set the repository variable `NUGET_USER` to your nuget.org profile name: `gh variable set NUGET_USER --body "<nuget-profile-name>"`.

## Update the Changelog

1. Open `CHANGELOG.md`. Move the entries under `Unreleased` into a new `x.y.z - YYYY-MM-DD` section.
2. Check that every bug fix, new feature and breaking change since the last tag has an entry. Mark breaking changes with **BREAKING** and describe the upgrade steps. A breaking change requires a new major version.
3. Merge the changelog update to `master` before you create the tag.

## Create Tag

1. Create an [annotated git tag](https://git-scm.com/book/en/v2/Git-Basics-Tagging) on the commit for the release. Such as `$ git tag -a v1.1.0 -m "Release v1.1.0"`
2. Push the tag to the repository. The tag must be on a commit that is on `master`, and the `v*` tag ruleset allows only repository admins to create it.
3. The release workflow checks the tag, then runs the test matrix on Linux and Windows. This takes several minutes. If the tests pass, the workflow builds the package and creates a draft GitHub release with the packages attached. Nothing is published yet.
4. Review the draft release and its packages. To back out, delete the draft release and the run stops there.
5. Approve the `nuget` environment in GitHub Actions. After approval, the workflow pushes the package to nuget.org and publishes the GitHub release. Both steps are irreversible, and a published release and its tag are immutable.
