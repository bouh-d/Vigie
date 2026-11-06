# Design

## Why keeping a laptop awake is not one setting

Three things put a laptop to sleep, and they need different handling.

1. **Idle timers** ("turn off the display after", "sleep after"). A power
   request (`PowerSetRequest(PowerRequestSystemRequired)`) suppresses them.
2. **User actions** — closing the lid, the power button, Start > Sleep. No API
   blocks these; Windows ends power requests when they happen. Only the power
   scheme's *lid close action* changes what closing the lid does.
3. **Protections** — critical battery, thermal shutdown, Windows Update restart.
   Vigie never touches these.

So Vigie holds a system power request for the whole session *and*, when asked to
work with the lid closed, changes one power-scheme value: the lid close action.

## Modern Standby

Most recent laptops use Modern Standby (S0 low-power idle) rather than S3 sleep.
Three consequences matter:

- On battery, Windows ends "system required" requests five minutes after the
  sleep timeout. A request alone does not hold on battery. Vigie therefore sets
  the battery sleep timeout to *Never* for the session on these machines — the
  one extra value it changes — and restores it at the end.
- On AC the request holds, so the AC sleep timeout is left alone. Windows can
  still log that it entered Modern Standby (event 506, *Idle Timeout*) once
  that timeout passes; with the request held nothing is suspended, as a laptop
  test with the lid closed for 20 minutes showed.
- A Modern Standby sleep may not raise the suspend and resume messages, so Vigie
  also detects sleep by comparing two clocks (below).

## The power request

`PowerCreateRequest` + `PowerSetRequest` is preferred because Windows releases
the request automatically if the process dies, and the reason is visible in
`powercfg /requests`. `SetThreadExecutionState` is the fallback when the request
cannot be created; it is bound to its calling thread, so it is called from a
thread that lives for the whole session. `ES_AWAYMODE_REQUIRED` is never used.

## Measuring time across sleep

The session deadline is an instant on `Environment.TickCount64`, which counts
time spent asleep and ignores clock changes. The remaining time is the deadline
minus the current tick count, so a session ends on schedule even if the machine
slept, and moving the system clock has no effect.

Sleep that the power messages missed is detected on the five-second beat:
`TickCount64` advances by real time while `QueryUnbiasedInterruptTime` skips the
time asleep. A gap larger than a minute between the two is time the machine was
frozen; the difference is how long it slept.

## Changing settings without leaving a trace

Vigie changes at most three values per power scheme — the lid action on AC and
on battery, and the battery sleep timeout on Modern Standby — and follows one
rule: write the journal before touching anything.

`%LOCALAPPDATA%\Vigie\restore.json` is written atomically (temporary file,
flush, rename) before the first change. It holds the scheme, subgroup and
setting identifiers, the original AC and DC values, the values Vigie applied, and
a state (`Pending`, `Applied`, `Restored`). An original is captured only once per
scheme, and any leftover journal is restored before a new session starts, so a
recorded original can never be a value Vigie itself set.

Restoring reads each value back: if it still holds what Vigie set, the original
is written back; if someone changed it in the meantime, their value is kept and
the difference is logged. Writes target the recorded scheme identifier, not "the
active scheme", and the scheme is re-activated only if it is the active one.

The active scheme is duplicated-and-activated by some tools; Vigie does not do
that. It would conflict with the Windows 11 power mode and with vendor utilities,
and it would lose the user's changes if they edited the scheme mid-session.

## Restore safety nets, fastest to slowest

1. **Normal end** — timer, stop, task finished, battery threshold, cap.
2. **Vigie closing and Windows signing out** — `Application.SessionEnding`
   restores before the process exits.
3. **Unhandled exceptions** — the dispatcher, app-domain and task-scheduler
   handlers restore, then let the process terminate.
4. **A watchdog process** — `Vigie.exe --watchdog <pid> <start>`, started without
   a window when a session changes a setting. It waits for the Vigie instance to
   exit and restores if the journal is still `Applied`; it exits by itself on a
   clean end, signalled through a named event.
5. **The next launch** of Vigie.
6. **The next sign-in** — a `RunOnce` value runs `Vigie.exe --restore-if-dirty`,
   which restores only if the instance named in the journal is no longer running.

A manual `Vigie.exe --restore`, and the equivalent `powercfg` procedure in the
README, cover the case where none of the above ran. A session that changes no
setting (lid handling off, on a machine without Modern Standby) has nothing to
restore and arms neither the watchdog nor the `RunOnce` value.

A journal that cannot be read, or whose values could not all be put back, blocks
the next session until it is dealt with: writing a new journal over it would lose
the originals it holds.

## Degraded mode

When the lid action is enforced by group policy, or writing it is denied, or
another program keeps changing it, Vigie stops managing the lid, keeps the system
awake, and shows a banner telling the user to keep the lid open. It never fights
another program: a setting changed by a third party is re-applied once, and on a
second change Vigie steps aside.

## Modern Standby compatibility

Some laptops go to sleep when the screen turns off even though a "system
required" request is held. For those, an advanced option, off by default, also
holds a display request while the lid is closed. A display request prevents the
idle lock as well, so with this option closing the lid always locks the session.
With an external display connected the display stays on, which is why the
option is opt-in.

## Telling a working session from an idle one

An open `claude.exe` is not a running task: a terminal session, or a tab of the
Claude desktop app, keeps its process open while it waits for the next prompt.
Vigie measures the CPU time of each Claude Code process and of everything it
started (tool commands, shells), and counts as work either 0.6 s of CPU within
30 s or a command starting or ending. Measured on a laptop, a session waiting
for a prompt uses up to about 50 ms every 3 s; one at work, several times more.

- A process shows as idle after two minutes without work. One seen for the
  first time shows as idle once a full 30-second window has passed quietly.
- The process tree comes from a Toolhelp snapshot. A parent identifier outlives
  its process and can be reused, so a child older than its parent is left out.
- The task mode ends when the followed process exits, or when it has done
  nothing for 10 minutes. Those minutes start at the wake session's start at
  the earliest, so a session that was already waiting gets the full limit, and
  the two-minute grace still follows.
- A `claude.exe` whose parent is the desktop app is labelled as such.

## The activity lock

An optional lock for a laptop left open during a session: once nobody has
touched the PC for 3 seconds, the first input locks the Windows session
(`LockWorkStation`) or suspends the PC. It is built so that nothing about it
resembles a keylogger:

- Activity is the time of the last input (`GetLastInputInfo`), the same signal
  Windows' idle timers use. There is no keyboard or mouse hook and no raw input.
- The shortcut that disarms it is a `RegisterHotKey` registration on the main
  window, made at the start of a session and removed at its end. A combination
  another application already holds is reported before the session starts.
- Pressing the shortcut starts with its modifier keys, which are input too. So
  when activity begins, the lock checks (`GetAsyncKeyState`) whether one of the
  shortcut's own modifiers is held: if so, the rest of the combination has two
  seconds; anything else locks at once. That check is the only keyboard state
  read.
- A shortcut needs two of Ctrl, Alt and Shift. With a single modifier it would
  take combinations such as Ctrl + C from every application for the length of
  the session.
- A Windows lock disarms it: the lock screen protects the PC by itself, and a key
  pressed there to sign in must not suspend it. After it fires, it also stays
  disarmed until the shortcut arms it again, so the user is not locked out a
  second time while reading the screen after signing in.

## Resuming after the usage limit

Claude Code reports the account's usage limit in its stream-json output. A
`rate_limit_event` carries its state (`status`, `resetsAt` in seconds since
1970, `rateLimitType`). When a request is refused, the assistant message and
the result both carry `api_error: "usage_limit_reached"`, and the assistant
message repeats the record under `api_error_params.rate_limit_info`.
`api_error` is marked internal and may change, so a run also counts as stopped
by the limit when it ends in error after a `rate_limit_event` whose status is
`rejected` and that extra usage does not cover.

The option is off by default because waiting keeps the PC awake. When it is on,
Vigie plans the resume when the process exits:

- at the reset time plus one minute, so a clock slightly ahead of the server's
  does not resume too early;
- only within the keep-awake session: in task mode the waiting task counts as
  running, and a resume that would fall after the session's end, or after the
  cap, is not planned;
- not when the reset is more than 6 hours away. A 5-hour limit fits; a weekly
  limit usually does not, and waiting for it would hold the PC for days;
- with `claude -p --resume <session id>`, the identifier from the run's `init`
  event, so the task keeps its history. Extra arguments that already pick a
  session (`--continue`, `--resume`, `--session-id`, `--fork-session`) prevent
  the resume rather than conflict with it;
- at most 3 times per task, so a limit that keeps refusing does not loop.

The plan lives in memory only: closing Vigie, interrupting the task, starting
another task or ending the session cancels it, and the log says why.

## How it is tested

- Unit tests cover the logic with in-memory fakes: the journal and its
  invariant, the restore rules, the state machine and its rollbacks, the
  deadline across sleep, the stream-json reader, the executable checks, the
  argument list, the activity lock, the resume after the usage limit and the
  settings file.
- On every push, continuous integration publishes the executable and runs it in
  a Windows desktop session (`tests/e2e/restore-checks.ps1`): it starts a
  session through UI Automation, checks the lid action and the power request
  with `powercfg`, and checks that the values come back after Stop, after Vigie
  is killed, after Vigie and the watchdog are both killed, and through the
  sign-in command.
- Lid close, sleep, battery and Modern Standby behaviour need a real laptop:
  see the manual plan in [TESTS.md](TESTS.md).
