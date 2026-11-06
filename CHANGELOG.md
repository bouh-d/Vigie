# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Keep the screen on** moved from the advanced settings to the main part of
  the window, next to the activity lock, which locks the PC that Windows no
  longer locks by itself while the screen is held on.

### Fixed

- Started from inside another app, such as a Claude Code session of the Claude
  desktop app, Vigie starts itself again through Explorer. That app kept
  Vigie's files and sign-in entry in its own storage, where the restore safety
  nets could not find them.
- A command Claude Code runs in the background, such as a local server or a
  wait on a build, keeps its session working even when it uses almost no CPU.
  Task mode no longer ends while such a command runs.

## [0.1.0] - 2026-11-06

### Added

- Fixed-duration wake sessions (15 minutes, 1, 2 or 4 hours, or a custom value
  from 5 minutes to 24 hours) that prevent idle sleep and hibernation.
- A mode that lasts until the Claude Code task finishes: it follows a process
  and stops two minutes after it exits, with an adjustable safety cap. A session
  that waits for its next prompt, such as a tab of the Claude desktop app,
  counts as finished after 10 minutes without activity.
- Lid-close handling: the lid action is set to *Do nothing* during a session and
  restored to its exact previous value afterwards.
- A restore journal with atomic writes and several safety nets (a watchdog
  process, a `RunOnce` entry and a startup check), so the power settings come
  back even after a crash or a forced shutdown. Each restore is written to the
  log.
- Modern Standby handling: the battery sleep timeout is neutralised for the
  session so keep-awake requests are not dropped on battery, and an advanced
  compatibility option holds a display request while the lid is closed for
  laptops that sleep when the screen turns off.
- Claude Code integration: detection of running processes, shown as working or
  idle, launch in autonomous or interactive mode, stream-json parsing, and a
  graceful interrupt.
- An option, off by default, to resume an autonomous task stopped by the usage
  limit once the limit resets.
- An activity lock: during a session, after 3 seconds without activity, the
  first mouse movement or key press locks the PC or puts it to sleep. A shortcut
  the user chooses turns it off and on.
- Battery monitoring with a configurable stop threshold, lock on lid close, and
  an optional sleep at the end of a session.
- A log shown in the window and written to a rotating local file.
- The window's choices, including the shortcut, are kept between launches.
- End-to-end restore checks that run the published executable on Windows in
  continuous integration.

[Unreleased]: https://github.com/bouh-d/vigie/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/bouh-d/vigie/releases/tag/v0.1.0
