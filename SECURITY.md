# Security Policy

## Supported versions

Security fixes go into the latest release only. If you use an older version, upgrade to the latest release first.

| Version | Supported |
|---------|-----------|
| Latest release on [nuget.org](https://www.nuget.org/packages/RecursiveDataAnnotationsValidation) | Yes |
| Older releases | No |

## Report a vulnerability

Do not open a public issue for a security problem.

Use GitHub's private reporting instead:

1. Open the [Security tab](https://github.com/tgharold/RecursiveDataAnnotationsValidation/security) of this repository.
2. Select **Report a vulnerability**.
3. Describe the problem and include a minimal model or code sample that reproduces it.

Please include:

- The package version and target framework (for example, `net8.0` or `net481`).
- What you expected to happen and what happened instead.
- The impact, if you know it.

This is a one-person project. I aim to reply within 30 days. I will tell you whether I accept the report and agree on a disclosure date with you. I will credit you in the release notes unless you ask me not to.

## What counts as a vulnerability

This library validates object graphs by reflection. It may run over input that an attacker controls, such as a bound request model. These are in scope:

- **Validation bypass.** An invalid object passes validation when it should fail.
- **Denial of service.** A crafted object graph crashes the process or uses unbounded time or memory. A stack overflow is an example.

These are not in scope:

- Bugs that have no security impact. Open a normal [issue](https://github.com/tgharold/RecursiveDataAnnotationsValidation/issues) for those.
- Vulnerabilities in your own validation attributes or in .NET itself. Report those to the code's owner.
- Exceptions shown to end users. This is backend code. The calling application must catch exceptions and decide what to display.
- Vulnerabilities in dependencies. Dependabot tracks those for this repository.

## Disclosure

After a fix ships, I publish a [GitHub security advisory](https://github.com/tgharold/RecursiveDataAnnotationsValidation/security/advisories) and add an entry to the [changelog](CHANGELOG.md).
