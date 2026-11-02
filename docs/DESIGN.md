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
Two consequences matter:

- On battery, Windows ends "system required" requests five minutes after the
  sleep timeout. A request alone does not hold on battery. Vigie therefore sets
  the battery sleep timeout to *Never* for the session on these machines — the
  one extra value it changes — and restores it at the end.
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

## How it is tested

- Unit tests cover the logic with in-memory fakes: the journal and its
  invariant, the restore rules, the state machine and its rollbacks, the
  deadline across sleep, the stream-json reader, the executable checks and the
  argument list.
- On every push, continuous integration publishes the executable and runs it in
  a Windows desktop session (`tests/e2e/restore-checks.ps1`): it starts a
  session through UI Automation, checks the lid action and the power request
  with `powercfg`, and checks that the values come back after Stop, after Vigie
  is killed, after Vigie and the watchdog are both killed, and through the
  sign-in command.
- Lid close, sleep, battery and Modern Standby behaviour need a real laptop:
  see the manual plan in [TESTS.md](TESTS.md).
