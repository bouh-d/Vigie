# Contributing

## Build and test

Vigie targets .NET 10 and builds on Windows. The core library and its tests
also build and run on Linux and macOS through `EnableWindowsTargeting`, which is
how continuous integration can run the unit tests without a Windows desktop.

```bash
dotnet build Vigie.sln -c Release
dotnet test Vigie.sln -c Release
dotnet format Vigie.sln --verify-no-changes
```

The unit tests never call a real power, process or clock API: `IPowerApi`,
`IProcessApi`, `IClock` and the other platform interfaces are replaced with
in-memory fakes. Anything that can only be checked on real hardware — lid close,
sleep, battery behaviour — belongs in the manual test plan in
[docs/TESTS.md](docs/TESTS.md), not in an automated test.

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
- Interface text is French; code, identifiers, comments and commit messages are
  English.

## Commits

One logical change per commit, with a message in the imperative mood following
[Conventional Commits](https://www.conventionalcommits.org/), for example:

```
fix(power): keep user change when restoring lid action
```
