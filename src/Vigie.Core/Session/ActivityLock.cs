using Vigie.Core.Logging;
using Vigie.Core.Platform;
using Vigie.Core.Power;

namespace Vigie.Core.Session;

public enum ActivityResponse
{
    Lock,
    Sleep,
}

public enum ActivityLockState
{
    /// <summary>No session, or the option is not ticked.</summary>
    Off,

    /// <summary>Waiting for the PC to go untouched for the quiet period.</summary>
    Arming,

    Armed,

    /// <summary>Activity came with the shortcut's modifier keys held: the rest
    /// of the shortcut may follow.</summary>
    AwaitingShortcut,

    /// <summary>Disarmed by the shortcut, after a response or by a Windows
    /// lock. The shortcut arms it again.</summary>
    Disarmed,
}

/// <summary>
/// Locks or suspends the PC when someone touches it during a session. It reads
/// the time of the last input, as Windows' idle timers do, and when activity
/// starts, whether the shortcut's modifier keys are held. It installs no
/// keyboard or mouse hook and never sees what is typed. The window registers
/// the shortcut and forwards each press: one disarms the lock, the next arms it
/// again. Driven from the UI thread, like the session.
/// </summary>
public sealed class ActivityLock(IPowerApi power, IKeyboard keyboard, ILog log, IClock clock)
{
    private ActivityResponse _response;
    private long _armedTick;
    private long _awaitingSinceTick;

    public event EventHandler? StateChanged;

    /// <summary>How long nobody may touch the PC before the lock arms, so the
    /// user can start a session and walk away.</summary>
    public static TimeSpan QuietPeriod { get; } = TimeSpan.FromSeconds(15);

    /// <summary>How long the rest of the shortcut may take once its modifier
    /// keys are down.</summary>
    public static TimeSpan ShortcutWindow { get; } = TimeSpan.FromSeconds(2);

    public ActivityLockState State { get; private set; } = ActivityLockState.Off;

    public Shortcut Shortcut { get; private set; } = Shortcut.Default;

    /// <summary>Whether activity is acted on now, which calls for frequent polling.</summary>
    public bool IsWatching => State is ActivityLockState.Armed or ActivityLockState.AwaitingShortcut;

    /// <summary>The time left before the lock arms, if nobody touches the PC.</summary>
    public TimeSpan TimeUntilArmed
    {
        get
        {
            if (State != ActivityLockState.Arming)
            {
                return TimeSpan.Zero;
            }

            TimeSpan left = QuietPeriod - power.GetTimeSinceLastInput();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public void Start(ActivityResponse response, Shortcut shortcut)
    {
        _response = response;
        Shortcut = shortcut;
        log.Info(SessionNarration.ActivityLockOn(shortcut));
        SetState(ActivityLockState.Arming);
    }

    public void Stop() => SetState(ActivityLockState.Off);

    public void Poll()
    {
        switch (State)
        {
            case ActivityLockState.Arming when power.GetTimeSinceLastInput() >= QuietPeriod:
                _armedTick = clock.TickCountMilliseconds;
                log.Info(SessionNarration.ActivityLockArmed(_response));
                SetState(ActivityLockState.Armed);
                break;

            // Pressing the shortcut starts with its modifier keys, which are
            // activity too: they get a moment for the rest of it.
            case ActivityLockState.Armed when TouchedSince(_armedTick):
                if (keyboard.IsAnyDown(Shortcut.Modifiers))
                {
                    _awaitingSinceTick = clock.TickCountMilliseconds;
                    SetState(ActivityLockState.AwaitingShortcut);
                }
                else
                {
                    Respond();
                }

                break;

            case ActivityLockState.AwaitingShortcut
                when !keyboard.IsAnyDown(Shortcut.Modifiers)
                    || clock.TickCountMilliseconds - _awaitingSinceTick >= ShortcutWindow.TotalMilliseconds:
                Respond();
                break;
        }
    }

    public void OnShortcut()
    {
        switch (State)
        {
            case ActivityLockState.Arming or ActivityLockState.Armed or ActivityLockState.AwaitingShortcut:
                log.Info(SessionNarration.ActivityLockDisarmed(Shortcut));
                SetState(ActivityLockState.Disarmed);
                break;
            case ActivityLockState.Disarmed:
                log.Info(SessionNarration.ActivityLockRearmed());
                SetState(ActivityLockState.Arming);
                break;
        }
    }

    /// <summary>
    /// The Windows lock screen protects the PC by itself, and a key pressed to
    /// sign in must not put it to sleep: a lock disarms until the shortcut.
    /// </summary>
    public void OnWorkstationLocked()
    {
        if (State is ActivityLockState.Arming or ActivityLockState.Armed or ActivityLockState.AwaitingShortcut)
        {
            log.Info(SessionNarration.ActivityLockPausedByWindowsLock(Shortcut));
            SetState(ActivityLockState.Disarmed);
        }
    }

    private bool TouchedSince(long tick)
        => clock.TickCountMilliseconds - (long)power.GetTimeSinceLastInput().TotalMilliseconds > tick;

    private void Respond()
    {
        // Disarmed first: the lock it causes comes back through
        // OnWorkstationLocked, which must find nothing left to do.
        SetState(ActivityLockState.Disarmed);
        if (_response == ActivityResponse.Lock)
        {
            log.Warning(SessionNarration.ActivityLocked(power.TryLockWorkstation()));
            return;
        }

        // Written before the call, which may return only once the PC wakes up.
        log.Warning(SessionNarration.ActivitySuspending());
        if (!power.TrySuspend())
        {
            log.Warning(SessionNarration.ActivitySuspendFailed());
        }
    }

    private void SetState(ActivityLockState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
