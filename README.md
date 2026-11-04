# Vigie

Vigie keeps a Windows laptop awake while a Claude Code task runs, including with
the lid closed. It restores every power setting it changes when the session
ends, including after a crash or a forced shutdown.

> The interface is in French. This file is also available in French:
> [README.fr.md](README.fr.md).

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
the journal are folded away below it.

1. Choose a duration: 15 minutes, 1, 2 or 4 hours, another duration, or until
   the Claude Code task finishes.
2. Leave **Fonctionner capot fermé** (work with the lid closed) ticked to keep
   running when you close the lid.
3. Optionally open **Claude Code**, fill in a project folder and prompt, pick
   **Tâche autonome** (autonomous task), and start Claude Code from Vigie.
4. Press **Démarrer** (start). Close the lid and leave.

The autonomous task mode is recommended with the lid closed: an interactive
session can stop to ask for permission, and nobody is there to answer.

**Réglages avancés** (advanced settings) holds the options that rarely change:
keeping the screen on, locking on lid close, sleeping at the end, the battery
threshold, the cap of the task mode, Modern Standby compatibility and the
activity lock. Vigie remembers these choices in
`%LOCALAPPDATA%\Vigie\settings.json`.

If your laptop goes to sleep when the screen turns off despite the session, tick
**Compatibilité Modern Standby** under **Réglages avancés**. Vigie then also keeps
the display active while the lid is closed, and always locks the session when you
close the lid.

### Activity lock

Tick **Verrouiller si quelqu'un touche le PC** (lock if someone touches the PC)
to protect a laptop left open during a session. Once nobody has touched it for
15 seconds, the first mouse movement or key press locks the Windows session, or
puts the PC to sleep if you chose that under **Réglages avancés**. Claude Code
keeps running while the session is locked; sleep suspends it.

A shortcut, Ctrl + Alt + V by default, disarms the lock when you come back and
arms it again when you leave. Press it before touching anything else: its
modifier keys get two seconds for the rest of the combination, and any other
activity triggers the lock at once. You can choose another combination under
**Réglages avancés**: two of Ctrl, Alt and Shift, plus a letter, a digit or F1
to F24. While a session runs, Windows gives that combination to Vigie alone.

The lock disarms when Windows locks, so a key pressed on the lock screen never
puts the PC to sleep. With the lid closed, the built-in keyboard and touchpad
send nothing, and only an external device can trigger the lock.

## What Vigie changes, and how it restores it

During a session Vigie changes at most three power-scheme values, records the
originals in `%LOCALAPPDATA%\Vigie\restore.json` before writing anything, and
puts them back at the end:

- the **lid close action**, on AC and on battery, is set to *Do nothing* (only
  when "work with the lid closed" is ticked and no group policy enforces it);
- on Modern Standby machines, the **battery sleep timeout** is set to *Never*,
  because Windows otherwise ends keep-awake requests five minutes after the
  sleep timeout on battery.

It never changes the display timeout, the AC sleep timeout, the power button,
critical-battery behaviour, hibernation or the active power scheme.

The settings are restored when the timer ends, on **Arrêter** (stop), when Vigie
closes, when Windows signs out, on an unhandled exception, and — if the process
is killed — by a companion watchdog process or, failing that, at the next launch
or the next sign-in.

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

The activity lock reads the time of the last input, as Windows' own idle timers
do, and whether its shortcut's modifier keys are held. It installs no keyboard
or mouse hook and records no keystroke. Its shortcut is registered with
`RegisterHotKey` only while a session runs.

This is an independent project, not affiliated with Anthropic. Claude and Claude
Code are trademarks of Anthropic.

## License

[MIT](LICENSE).
