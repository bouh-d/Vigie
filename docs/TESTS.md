# Manual test plan

The unit tests cover the logic without touching the system; on Windows a few
more check every native import and run the watchdog against a real process.
Continuous integration also runs `tests/e2e/restore-checks.ps1` on a Windows
runner, which covers T5, T6 and T8 and the command run at sign-in for T7 (a
virtual machine has no lid or battery, so it checks the restore paths, not the
lid behaviour).
The other cases can only be checked on real laptops. Record each run in the
results table at the bottom.

## Before the first run

Note the starting values, so they can be put back by hand if anything goes
wrong:

```powershell
powercfg /qh SCHEME_CURRENT SUB_BUTTONS LIDACTION
powercfg /qh SCHEME_CURRENT SUB_SLEEP STANDBYIDLE
```

`/qh` also lists hidden settings: some laptops hide the lid action, and
`powercfg /q` then prints nothing for it.

Run T6 and T5 first: they confirm the settings come back before you rely on
Vigie with the lid closed. Then T1 and T2.

## Verification tools

- `powercfg /qh SCHEME_CURRENT SUB_BUTTONS LIDACTION` and
  `powercfg /qh SCHEME_CURRENT SUB_SLEEP STANDBYIDLE`: the values before and
  after a session.
- `powercfg /a`: whether the machine supports Modern Standby (an S0 low-power
  idle state is listed as available).
- `powercfg /requests` in an administrator terminal: Vigie's request and its
  reason.
- `powercfg /sleepstudy` in an administrator terminal on Modern Standby: the
  history of sleep sessions.
- System log, Kernel-Power source: 42 (enter sleep), 107 (resume), 506 and 507
  (enter and exit Modern Standby):

  ```powershell
  Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; Id=42,107,506,507} -MaxEvents 20
  ```

  On a Modern Standby laptop on AC, Windows can log 506 with the reason *Idle
  Timeout* once the AC sleep timeout has passed, although Vigie's request
  holds. Nothing is suspended then. Check that work went on, with a process
  that writes the time every few seconds or with Vigie's journal, which reports
  any real sleep: a 506 is a failure only when activity stopped.

## Cases

| ID  | Scenario | Expected result |
| --- | --- | --- |
| T1  | AC, lid closed, 2-hour session, 30-minute autonomous task | Task finishes, no event 42, no pause in activity (see the note on 506), settings restored at the end |
| T2  | Battery, Modern Standby machine, lid closed 60 min, Windows sleep timeout on battery set to 5 min | No sleep; `STANDBYIDLE` on battery restored at the end |
| T3  | Battery, S3 machine, lid closed 60 min | No sleep |
| T4  | Unplug during a lid-closed session | No sleep, battery banner, journal entry |
| T5  | Timer ends, lid open | `powercfg /q` values identical to before the session |
| T6  | `taskkill /F /PID <Vigie pid>` during a session | The watchdog restores within 5 s |
| T7  | Force-kill Vigie and the watchdog, then restart Windows | Restore at sign-in, `RunOnce` value gone |
| T8  | As T7 but relaunch Vigie without restarting | Restore at launch and a message naming the restored values |
| T9  | Press the power button during a session | On resume: sleep logged with its duration, requests recreated, timer correct |
| T10 | Battery below the threshold | Session stopped, settings restored, notification |
| T11 | Task mode: a normal task, then a stuck task | End 2 min after exit; stop at the cap for the stuck task |
| T12 | "Interrupt" on an autonomous task | No process of the tree left after 15 s |
| T13 | Claude desktop app open and Claude Code in a terminal | Only Claude Code is listed |
| T14 | Trap prompts: `" & calc & "`, `; calc`, `%PATH%` | Prompt passed through unchanged, no other program started |
| T15 | Group policy on the lid action (gpedit.msc, Windows Pro or Enterprise) | Degraded mode, no write, banner shown |
| T16 | Standard account, no administrator rights | Everything works, no UAC prompt |
| T17 | Sign out or shut down Windows during a session | Settings restored, verified at restart |
| T18 | Change the power scheme during the session | Both schemes return to their original values |
| T19 | Change the lid value by hand during the session, then end the session within a minute | Restore does not overwrite the user's choice |
| T20 | Close the window during a session | "Minimise / Stop and quit / Cancel" choice |
| T21 | Activity lock on, lid open: leave the PC untouched 3 s, then move the mouse | Session locked within a second, journal entry, Claude Code still running |
| T22 | Activity lock armed: press the shortcut, use the PC, press it again, leave it 3 s | Disarmed without locking, then armed again, a notification each time |
| T23 | Activity lock set to sleep: start a session, lock Windows with Win + L within 3 seconds, then press a key on the lock screen | The PC does not sleep |
| T24 | Task mode on a Claude desktop app session: let its task finish and leave the tab open | Listed as idle about 2 min later; the wake session ends about 12 min after the task, with a log line naming the idle task |
| T25 | Task mode, **Resume after the usage limit resets** ticked: an autonomous task started from Vigie reaches the 5-hour limit | Log lines with the reset time and the resume time; the session keeps running; a minute after the reset the task resumes with the same Claude Code session ID in the log |
| T26 | Task mode on a Claude Code session that starts a quiet background command (for example a 15-minute wait) and goes back to waiting for a prompt | Listed as working while the command runs, and the wake session goes on past 10 minutes; idle about 2 min after the command ends |
| T27 | **Keep the screen on** and the activity lock together, lid open: leave the PC untouched 5 minutes, then move the mouse | The screen stays on the whole time, then the first movement locks the PC |

A value changed during a session is re-applied once at the next periodic check,
which runs every minute; only a second change makes Vigie stop managing it.
That is why T19 ends the session within a minute of the change.

If T1 or T2 fails because the laptop sleeps when the screen turns off, tick
**Modern Standby compatibility** under **Advanced settings** and run them again.

## Coverage

Run T1 to T20 on Windows 11 x64. Run T1, T2, T5, T6 and T9 on Windows 10 as
well, and on ARM64 if a machine is available.

## Results

| Date | Model | Windows version | Modern Standby or S3 | Cases | Result |
| --- | --- | --- | --- | --- | --- |
| 2026-11-05 | Samsung 940XGK | Windows 11 Home x64, build 26200 | Modern Standby | T5, T6, T8 | Pass. T5: values identical after the timer ended. T6: restored by the watchdog 230 ms after the kill. T8: restored 390 ms after the relaunch, with the startup message. |
| 2026-11-05 | Samsung 940XGK | Windows 11 Home x64, build 26200 | Modern Standby | T21, T22, T23 | Pass. T21: the first mouse movement locked the PC once the lock was on. T22: the shortcut turned the lock off and back on, with a notification each time. T23: with the sleep action, signing in on the lock screen did not put the PC to sleep. |
| 2026-11-05 | Samsung 940XGK | Windows 11 Home x64, build 26200 | Modern Standby | T1, short form: lid closed 20 min on AC, no task | Pass. A process writing the time every 10 s ran without a pause (largest gap 11 s) and Vigie detected no sleep. Windows logged 506 (Idle Timeout) 3.5 min after the lid closed and 507 when it opened. |
