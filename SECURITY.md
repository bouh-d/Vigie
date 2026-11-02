# Security policy

## Supported versions

Security fixes are provided for the latest released version. Vigie is
pre-1.0; older versions are not maintained.

| Version | Supported |
| ------- | --------- |
| 0.1.x   | Yes       |

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub's
[private vulnerability reporting](https://github.com/bouh-d/vigie/security/advisories/new)
rather than opening a public issue. Include the affected version, the platform,
and the steps to reproduce.

You can expect an initial response within a few days. Once a fix is available it
is released and the advisory is published.

## Scope

Vigie runs without elevation, installs no service or driver, and makes no
network request. The security-relevant surface is the set of power-scheme values
it writes, the `RunOnce` value under `HKCU`, the files under
`%LOCALAPPDATA%\Vigie`, and the way it locates and launches the Claude Code
executable. Reports about any of these are in scope.
