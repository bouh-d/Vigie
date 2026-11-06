# Vigie

Vigie keeps a Windows 10 or 11 laptop awake while a Claude Code task runs,
including with the lid closed. Start a long task, close the lid and leave: the
laptop does not go to sleep until the task is done. Every power setting Vigie
changes is put back when the session ends, even after a crash or a forced
shutdown.

> This file is also available in French: [README.fr.md](README.fr.md).

<!-- Screenshot: docs/screenshot.png. The restore checks in continuous
     integration take one of a running session (artifact "restore-checks"). -->

## What Vigie cannot do

- run Claude Code during sleep, hibernation or shutdown;
- override the power button, a critical-battery event, a thermal shutdown or a
  Windows Update restart;
- work around firmware that forces sleep on lid close (rare);
- keep the network up if the laptop leaves Wi-Fi range;
- prevent overheating in a bag or an enclosed space.

If the machine sleeps anyway, Vigie detects it and states it plainly (time,
duration, effect on Claude Code) instead of showing "running".

## Requirements

- Windows 10 version 1809 or later, or Windows 11 (x64 or ARM64).
- [Claude Code](https://code.claude.com/docs/en/setup) installed and signed in.
  Vigie looks for the native installer location
  (`%USERPROFILE%\.local\bin\claude.exe`) and the executables on `PATH`.

## Install

### From a release

1. Download the archive for your architecture from the
   [releases page](https://github.com/bouh-d/vigie/releases).
2. Verify its checksum against `SHA256SUMS.txt`:

   ```powershell
   Get-FileHash .\Vigie-win-x64.zip -Algorithm SHA256
   ```

3. Vigie is not code-signed, so Windows SmartScreen shows a warning the first
   time you run it. Choose **More info**, then **Run anyway**, after you have
   verified the checksum.
4. Extract the archive and run `Vigie.exe`. No installation and no administrator
   rights are required.

### From source

```powershell
dotnet publish src/Vigie.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Replace `win-x64` with `win-arm64` for ARM64. The build requires the .NET 10
SDK.

## Usage

The window shows what a session needs. Claude Code, the advanced settings and
the log are folded away below it.

1. Choose a duration: 15 minutes, 1, 2 or 4 hours, a custom one, or until the
   Claude Code task ends.
2. Leave **Keep running with the lid closed** ticked.
3. Optionally open **Claude Code**, fill in a project folder and prompt, pick
   **Autonomous task**, and start Claude Code from Vigie.
4. Press **Start**. Close the lid and leave.

The autonomous task mode is recommended with the lid closed: an interactive
session can stop to ask for permission, and nobody is there to answer.

The **Claude Code** section shows each process as working or idle. A session
that stays open after its task, such as a tab of the Claude desktop app, shows
as idle once it has done nothing for about two minutes. When a session follows
such a task, the task counts as finished after 10 minutes without activity.

**Advanced settings** holds the other options. Vigie keeps your choices in
`%LOCALAPPDATA%\Vigie\settings.json`.

If your laptop goes to sleep when the screen turns off despite the session, tick
**Modern Standby compatibility** under **Advanced settings**. Vigie then also
keeps the display active while the lid is closed, and always locks the session
when you close the lid.

### Activity lock

Tick **Lock if someone touches the PC** for a laptop left open during a
session. After 3 seconds without activity, the first mouse movement or key
press locks the PC, and Claude Code keeps running. Under **Advanced settings**
you can choose sleep instead, which pauses Claude Code.

Ctrl + Alt + V turns the lock off when you come back and on again when you
leave. Type it before touching anything else. To change it, click the shortcut
field under **Advanced settings** and press the new keys: two of Ctrl, Alt and
Shift, plus a letter, a digit or F1 to F24.

### Resume after the usage limit

When an autonomous task started from Vigie stops because your Claude usage
limit is reached, Vigie can start it again once the limit resets. Tick
**Resume after the usage limit resets** under **Claude Code**. It is off by
default because the PC stays awake until the reset, which uses battery.

A minute after the reset time Claude Code reports, Vigie resumes the task's own
Claude Code session and asks it to continue. It waits only during the
keep-awake session, so choose **Until the Claude Code task ends**: the task
then counts as running while it waits, up to the cap. Vigie does not wait for a
limit that resets more than 6 hours later, such as a weekly one, and resumes a
task at most 3 times. **Interrupt**, starting another task, ending the session
or closing Vigie cancels the wait.

## What Vigie changes, and how it restores it

During a session Vigie changes at most three power-scheme values, records the
originals in `%LOCALAPPDATA%\Vigie\restore.json` before writing anything, and
puts them back at the end:

- the **lid close action**, on AC and on battery, is set to *Do nothing* (only
  when **Keep running with the lid closed** is ticked and no group policy
  enforces it);
- on Modern Standby machines, the **battery sleep timeout** is set to *Never*,
  because Windows otherwise ends keep-awake requests five minutes after the
  sleep timeout on battery.

It never changes the display timeout, the AC sleep timeout, the power button,
critical-battery behaviour, hibernation or the active power scheme.

The settings are restored when the timer ends, on **Stop**, when Vigie closes,
when Windows signs out and on an unhandled exception. If the process is killed,
a companion watchdog process restores them, or failing that the next launch or
the next sign-in.

If a session was interrupted and the settings were not restored, run:

```powershell
Vigie.exe --restore
```

As a last resort you can restore the lid action by hand with `powercfg`, using
the original values from `restore.json` (lid action: 0 do nothing, 1 sleep,
2 hibernate, 3 shut down; sleep timeout in seconds, 0 means never):

```powershell
powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 1200
powercfg /setactive SCHEME_CURRENT
```

Some laptops hide the lid action from `powercfg /q`. To read it there,
`powercfg /qh SCHEME_CURRENT SUB_BUTTONS LIDACTION` also lists hidden settings.

## Uninstall

1. If a session was interrupted, run `Vigie.exe --restore` once.
2. Delete the application folder and `%LOCALAPPDATA%\Vigie`.

Vigie installs no service, driver or scheduled task. The only startup entry it
uses is a `RunOnce` value under `HKCU`, created when a session changes a power
setting and removed once the settings are restored.

## Privacy

Vigie makes no network request and has no telemetry. Logs and Claude Code output
stay in `%LOCALAPPDATA%\Vigie` and are deleted after 14 days. That output can
contain code and secrets from your project; it is kept locally and nowhere else.
Vigie never reads or stores Claude Code credentials.

## Transparency

Besides the Claude Code tasks shown in the interface, the only windowless process
Vigie starts is the restore watchdog, and the only startup entry is the `RunOnce`
value described above. Both exist so the power settings are restored if Vigie is
killed. There is no obfuscation, no executable packing and no process injection.

The activity lock reads only the time of the last input and whether its
shortcut's modifier keys are held. It installs no keyboard or mouse hook and
records no keystroke. Its shortcut is registered with `RegisterHotKey` during a
session only.

This is an independent project, not affiliated with Anthropic. Claude and Claude
Code are trademarks of Anthropic.

## License

[MIT](LICENSE).
