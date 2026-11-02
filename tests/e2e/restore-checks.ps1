<#
.SYNOPSIS
Runs the published Vigie.exe in a real Windows session and checks that the
power settings it changes always come back: after Stop, after Vigie is killed
(the watchdog), when Vigie and the watchdog are both killed (the next launch),
and through the sign-in command. It drives the window through UI Automation,
reads the values with powercfg, and saves a screenshot of a running session.

The machine needs an interactive desktop and administrator rights for
powercfg /requests and /attributes; GitHub's Windows runners have both. It
changes the lid action of the active power scheme and makes that setting
visible, so run it on a disposable machine.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Executable,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$Executable = (Resolve-Path $Executable).Path
$startedAt = Get-Date
$journalPath = Join-Path $env:LOCALAPPDATA 'Vigie\restore.json'
$runOnceKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
$Automation = [System.Windows.Automation.AutomationElement]

function Invoke-Powercfg {
    $output = & powercfg @args
    if ($LASTEXITCODE -ne 0) {
        throw "powercfg $($args -join ' ') failed with $LASTEXITCODE`: $($output -join ' ')"
    }
    $output
}

function Get-LidAction {
    $text = (Invoke-Powercfg /q SCHEME_CURRENT SUB_BUTTONS LIDACTION) -join "`n"
    $ac = [regex]::Match($text, 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)')
    $dc = [regex]::Match($text, 'Current DC Power Setting Index:\s*0x([0-9a-fA-F]+)')
    if (-not ($ac.Success -and $dc.Success)) {
        throw "Could not read the lid close action:`n$text"
    }
    [pscustomobject]@{
        Ac = [Convert]::ToInt32($ac.Groups[1].Value, 16)
        Dc = [Convert]::ToInt32($dc.Groups[1].Value, 16)
    }
}

function Set-LidAction([int] $Value) {
    Invoke-Powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION $Value | Out-Null
    Invoke-Powercfg /setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION $Value | Out-Null
    Invoke-Powercfg /setactive SCHEME_CURRENT | Out-Null
}

function Test-Applied {
    $value = Get-LidAction
    $value.Ac -eq 0 -and $value.Dc -eq 0
}

function Test-Original {
    $value = Get-LidAction
    $value.Ac -eq $script:original.Ac -and $value.Dc -eq $script:original.Dc
}

# The condition runs inside this function, where $Condition, $TimeoutSeconds,
# $What and $deadline hide any caller variable with the same name.
function Wait-Until([scriptblock] $Condition, [int] $TimeoutSeconds, [string] $What) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out after $TimeoutSeconds s waiting for $What."
}

function Test-RequestHeld {
    $text = (Invoke-Powercfg /requests) -join "`n"
    $system = [regex]::Match($text, '(?s)SYSTEM:(.*?)AWAYMODE:').Groups[1].Value
    $system -match 'Vigie\.exe'
}

function Get-VigieProcesses {
    Get-CimInstance Win32_Process -Filter "Name = 'Vigie.exe'"
}

function Get-Watchdog {
    Get-VigieProcesses | Where-Object { $_.CommandLine -match '--watchdog' }
}

function Stop-AllVigie {
    # The watchdog goes first: killed second, it would restore in the gap.
    Get-Watchdog | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Get-VigieProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Wait-Until { -not (Get-VigieProcesses) } 10 'every Vigie process to exit'
}

function Find-Element($Parent, [string] $AutomationId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($Automation::AutomationIdProperty, $AutomationId)
    $element = $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $element) { throw "No element with the automation id $AutomationId." }
    $element
}

function Open-Vigie {
    $process = Start-Process -FilePath $Executable -PassThru
    # Windows PowerShell only keeps the exit code of a process whose handle
    # was opened before it exited.
    $null = $process.Handle
    $byProcess = New-Object System.Windows.Automation.PropertyCondition($Automation::ProcessIdProperty, $process.Id)
    $script:window = $null
    try {
        Wait-Until {
            if ($process.HasExited) {
                throw "Vigie exited with code $($process.ExitCode) before showing its window."
            }
            $script:window = $Automation::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $byProcess)
            $null -ne $script:window
        } 90 'the Vigie window'
    }
    catch {
        if (-not $process.HasExited) {
            $process.Refresh()
            Write-Host "Vigie is running in session $($process.SessionId), main window $($process.MainWindowHandle) '$($process.MainWindowTitle)'."
        }
        throw
    }

    # A runner's screen can be smaller than the window; fit it so the whole
    # window is on screen for the screenshot.
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $transform = $script:window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $transform.Move($area.X, $area.Y)
    $transform.Resize(620, [Math]::Min(860, $area.Height))

    # Nothing in this run may lock or suspend the machine, whatever the
    # virtual hardware reports about its lid.
    foreach ($id in 'LockOnLidClose', 'SuspendAtEnd') {
        $toggle = (Find-Element $script:window $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
    }

    $process
}

function Invoke-Button([string] $AutomationId) {
    $button = Find-Element $script:window $AutomationId
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Start-Session {
    Invoke-Button 'StartSession'
    Wait-Until { Test-Applied } 15 'the lid action to be set to Do nothing'
    Wait-Until { Test-Path $journalPath } 5 'the restore journal'
    Wait-Until { $null -ne (Get-Watchdog) } 10 'the watchdog process'
    # A watchdog that cannot find its session exits at once, leaving the
    # session without crash protection.
    Start-Sleep -Seconds 3
    if ($null -eq (Get-Watchdog)) { throw 'The watchdog exited while the session was running.' }
    if (-not (Get-ItemProperty -Path $runOnceKey -Name VigieRestore -ErrorAction SilentlyContinue)) {
        throw 'The RunOnce value was not created at the start of the session.'
    }
    if (-not (Test-RequestHeld)) {
        throw 'powercfg /requests does not list the Vigie request.'
    }
}

function Save-Screenshot([string] $Path) {
    $rect = $script:window.Current.BoundingRectangle
    $bitmap = New-Object System.Drawing.Bitmap ([int]$rect.Width), ([int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-Diagnostics {
    $current = [Diagnostics.Process]::GetCurrentProcess()
    Write-Host "This script runs in session $($current.SessionId), interactive: $([Environment]::UserInteractive)."

    $log = Join-Path $env:LOCALAPPDATA 'Vigie\logs\vigie.log'
    if (Test-Path $log) {
        Write-Host '::group::Vigie log'
        Get-Content $log | ForEach-Object { Write-Host $_ }
        Write-Host '::endgroup::'
    }

    Write-Host '::group::Vigie processes'
    Get-VigieProcesses | ForEach-Object { Write-Host "$($_.ProcessId) started $($_.CreationDate): $($_.CommandLine)" }
    Write-Host '::endgroup::'

    if (Test-Path $journalPath) {
        Write-Host '::group::Restore journal'
        Get-Content $journalPath | ForEach-Object { Write-Host $_ }
        Write-Host '::endgroup::'
    }

    try {
        $lid = Get-LidAction
        Write-Host "Lid action now $($lid.Ac)/$($lid.Dc), originally $($script:original.Ac)/$($script:original.Dc)."
    }
    catch {
        Write-Host "The lid action could not be read: $_"
    }

    # A crash that ends the process before Vigie can log it is still recorded
    # by the .NET runtime in the Application event log.
    $filter = @{ LogName = 'Application'; ProviderName = '.NET Runtime', 'Application Error'; StartTime = $startedAt }
    try { $events = @(Get-WinEvent -FilterHashtable $filter -ErrorAction Stop) }
    catch { $events = @() }
    if ($events.Count -gt 0) {
        Write-Host '::group::Application event log'
        $events | ForEach-Object { Write-Host "$($_.TimeCreated) $($_.ProviderName) $($_.Id)"; Write-Host $_.Message }
        Write-Host '::endgroup::'
    }
}

function Assert-Restored([string] $Case) {
    if (-not (Test-Original)) {
        $value = Get-LidAction
        throw "${Case}: the lid action is $($value.Ac)/$($value.Dc), expected $($script:original.Ac)/$($script:original.Dc)."
    }
    if (Test-Path $journalPath) { throw "${Case}: the restore journal was left behind." }
    if (Get-ItemProperty -Path $runOnceKey -Name VigieRestore -ErrorAction SilentlyContinue) {
        throw "${Case}: the RunOnce value was left behind."
    }
    Write-Host "${Case}: settings restored."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
# .NET methods such as Bitmap.Save resolve a relative path against the process
# directory, not the PowerShell location.
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

try {
    Stop-AllVigie

    # A machine without a lid, such as a CI runner, hides the setting from
    # powercfg /q. Showing it leaves its value unchanged.
    Invoke-Powercfg /attributes SUB_BUTTONS LIDACTION '-ATTRIB_HIDE' | Out-Null

    # A known starting point the session has to change and give back.
    Set-LidAction 1
    $script:original = Get-LidAction

    # Stop: the timer's normal path.
    $vigie = Open-Vigie
    Start-Session
    Start-Sleep -Seconds 3
    Save-Screenshot (Join-Path $OutputDirectory 'screenshot.png')
    Invoke-Button 'StopSession'
    Wait-Until { Test-Original } 15 'the lid action to come back after Stop'
    Wait-Until { $null -eq (Get-Watchdog) } 10 'the watchdog to exit after a clean end'
    if (Test-RequestHeld) { throw 'Stop: the power request is still held.' }
    Assert-Restored 'Stop'

    # Vigie killed: the watchdog restores within five seconds.
    Start-Session
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Stop-Process -Id $vigie.Id -Force
    Wait-Until { (Test-Original) -and -not (Test-Path $journalPath) } 15 'the watchdog to restore'
    $clock.Stop()
    if ($clock.Elapsed.TotalSeconds -gt 5) {
        throw "Forced kill: the watchdog took $([Math]::Round($clock.Elapsed.TotalSeconds, 1)) s."
    }
    Assert-Restored "Forced kill ($([int]$clock.Elapsed.TotalMilliseconds) ms)"

    # Vigie and the watchdog killed: the next launch restores.
    Open-Vigie | Out-Null
    Start-Session
    Stop-AllVigie
    if (-not (Test-Applied)) { throw 'The values should still be applied with every Vigie process gone.' }
    Start-Process -FilePath $Executable | Out-Null
    Wait-Until { (Test-Original) -and -not (Test-Path $journalPath) } 60 'the next launch to restore'
    Stop-AllVigie
    Assert-Restored 'Next launch'

    # The same, through the command the RunOnce value runs at the next sign-in.
    Open-Vigie | Out-Null
    Start-Session
    $command = (Get-ItemProperty -Path $runOnceKey -Name VigieRestore).VigieRestore
    if ($command -notmatch '--restore-if-dirty$') { throw "Unexpected RunOnce command: $command" }
    Stop-AllVigie
    $restore = Start-Process -FilePath $Executable -ArgumentList '--restore-if-dirty' -PassThru -Wait
    if ($restore.ExitCode -ne 0) { throw "--restore-if-dirty exited with $($restore.ExitCode)." }
    Assert-Restored 'Sign-in command'

    Write-Host 'All restore checks passed.'
}
catch {
    Write-Diagnostics
    throw
}
finally {
    # Vigie's own journal shows what it did at each step.
    $log = Join-Path $env:LOCALAPPDATA 'Vigie\logs\vigie.log'
    if (Test-Path $log) { Copy-Item $log (Join-Path $OutputDirectory 'vigie.log') }
    Stop-AllVigie
}
