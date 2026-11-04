# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- An activity lock: during a session, after 15 seconds without activity, the
  first mouse movement or key press locks the PC or puts it to sleep. A shortcut
  the user chooses turns it off and on.
- The window's choices, including the shortcut, are kept between launches.
- The log names the sleep model (Modern Standby or S3) at startup.

### Changed

- The interface and the log are in English.
- The window shows only what a session needs; Claude Code, the advanced
  settings and the log are folded away below it.
- The Modern Standby notice appears on battery only, where it applies.
- The manual test plan reads the values with `powercfg /qh`, which also lists
  settings a laptop hides.

### Fixed

- The battery charge is shown on AC power as well as on battery.
- Claude Code sessions that wait for their next prompt, such as tabs of the
  Claude desktop app, are shown as idle instead of running. In task mode, such
  a session counts as finished after 10 minutes without activity instead of
  keeping the PC awake until the cap.

## [0.1.0]

### Added

- Fixed-duration wake sessions (15 minutes, 1, 2 or 4 hours, or a custom value
  from 5 minutes to 24 hours) that prevent idle sleep and hibernation.
- A "until the Claude Code task finishes" mode that follows a process and stops
  two minutes after it exits, with an adjustable safety cap.
- Lid-close handling: the lid action is set to *Do nothing* during a session and
  restored to its exact previous value afterwards.
- A restore journal with atomic writes and several safety nets — a watchdog
  process, a `RunOnce` entry and a startup check — so the power settings come
  back even after a crash or a forced shutdown. Each restore is written to the
  log.
- Modern Standby handling: the battery sleep timeout is neutralised for the
  session so keep-awake requests are not dropped on battery, and an advanced
  compatibility option holds a display request while the lid is closed for
  laptops that sleep when the screen turns off.
- Claude Code integration: detection of running processes, launch in autonomous
  or interactive mode, stream-json parsing, and a graceful interrupt.
- Battery monitoring with a configurable stop threshold, lock on lid close, and
  an optional sleep at the end of a session.
- A journal shown in the interface and written to a rotating local file.
- End-to-end restore checks that run the published executable on Windows in
  continuous integration.

[Unreleased]: https://github.com/bouh-d/vigie/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/bouh-d/vigie/releases/tag/v0.1.0
