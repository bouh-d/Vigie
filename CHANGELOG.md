# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
  back even after a crash or a forced shutdown.
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
