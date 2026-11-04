# Contributing

## Build and test

Vigie targets .NET 10 and builds on Windows. The whole solution also builds on
Linux through `EnableWindowsTargeting`, and the unit tests run there, which is
convenient when you do not have a Windows machine at hand; the few that need
Windows itself are skipped. Continuous integration runs on Windows.

```bash
dotnet build Vigie.sln -c Release
dotnet test Vigie.sln -c Release
dotnet format Vigie.sln --verify-no-changes
```

The unit tests never call a real power, process or clock API: `IPowerApi`,
`IProcessApi`, `IClock` and the other platform interfaces are replaced with
in-memory fakes.

`tests/e2e/restore-checks.ps1` drives the published executable on a real Windows
desktop and checks that the power settings come back after Stop and after Vigie
is killed. CI runs it on every push. It changes the lid action of the active power
scheme, so only run it yourself on a machine you do not mind changing.

Anything that needs real hardware — lid close, sleep, battery behaviour — belongs
in the manual test plan in [docs/TESTS.md](docs/TESTS.md).

## Project layout

- `src/Vigie.Core` — the logic, with no WPF dependency. `Interop` holds the
  P/Invoke declarations; `Platform` the OS-service interfaces and their Win32
  implementations; `Power`, `ClaudeCode`, `Session`, `Logging` and `Storage`
  the features.
- `src/Vigie.App` — the WPF interface.
- `tests/Vigie.Core.Tests` — xUnit tests.

See [docs/DESIGN.md](docs/DESIGN.md) for the power strategy and the restore
safety nets.

## Style

- Warnings are treated as errors; keep the build clean.
- Run `dotnet format` before committing.
- Comments explain a *why* — a Windows constraint, a pitfall, a link to the
  Microsoft documentation — not what the next line does.
- Everything is in English: interface text, log lines, code, identifiers,
  comments and commit messages. Only `README.fr.md` is in French.

## Commits

One logical change per commit, with a message in the imperative mood following
[Conventional Commits](https://www.conventionalcommits.org/), for example:

```
fix(power): keep user change when restoring lid action
```
