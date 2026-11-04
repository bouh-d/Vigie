using Vigie.Core.Platform;
using Vigie.Core.Session;
using Vigie.Core.Tests.Fakes;
using Xunit;

namespace Vigie.Core.Tests;

public class ActivityLockTests
{
    private static readonly Shortcut CtrlAltV = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 'V');

    private readonly FakePowerApi _power = new();
    private readonly FakeKeyboard _keyboard = new();
    private readonly FakeClock _clock = new();
    private readonly RecordingLog _log = new();
    private readonly ActivityLock _lock;

    public ActivityLockTests()
    {
        _lock = new ActivityLock(_power, _keyboard, _log, _clock);

        // The click on the start button is the last input.
        _power.TimeSinceLastInput = TimeSpan.Zero;
    }

    [Fact]
    public void ItArmsOnlyOnceThePcHasGoneUntouchedForTheQuietPeriod()
    {
        _lock.Start(ActivityResponse.Lock, CtrlAltV);

        Untouched(ActivityLock.QuietPeriod - TimeSpan.FromSeconds(1));
        _lock.Poll();
        Assert.Equal(ActivityLockState.Arming, _lock.State);
        Assert.Equal(TimeSpan.FromSeconds(1), _lock.TimeUntilArmed);

        Untouched(TimeSpan.FromSeconds(1));
        _lock.Poll();
        Assert.Equal(ActivityLockState.Armed, _lock.State);
    }

    [Fact]
    public void ActivityWhileArmingPushesTheArmingBack()
    {
        _lock.Start(ActivityResponse.Lock, CtrlAltV);
        Untouched(TimeSpan.FromSeconds(10));

        Touch();
        Untouched(TimeSpan.FromSeconds(10));
        _lock.Poll();

        Assert.Equal(ActivityLockState.Arming, _lock.State);
        Assert.Equal(0, _power.LockCount);
    }

    [Fact]
    public void TheFirstActivityOnceArmedLocksTheSession()
    {
        Arm(ActivityResponse.Lock);

        Touch();
        _lock.Poll();

        Assert.Equal(1, _power.LockCount);
        Assert.Equal(0, _power.SuspendCount);
        Assert.Equal(ActivityLockState.Disarmed, _lock.State);
        Assert.True(_log.Contains("PC locked"));
    }

    [Fact]
    public void TheSleepResponseSuspendsThePcInstead()
    {
        Arm(ActivityResponse.Sleep);

        Touch();
        _lock.Poll();

        Assert.Equal(1, _power.SuspendCount);
        Assert.Equal(0, _power.LockCount);
    }

    [Fact]
    public void ItRespondsOnceThenWaitsForTheShortcut()
    {
        Arm(ActivityResponse.Lock);
        Touch();
        _lock.Poll();

        Touch();
        _lock.Poll();

        Assert.Equal(1, _power.LockCount);
    }

    [Fact]
    public void TheShortcutDisarmsWhenItsModifiersComeFirst()
    {
        Arm(ActivityResponse.Lock);

        _keyboard.Held = ShortcutModifiers.Control;
        Touch();
        _lock.Poll();
        Assert.Equal(ActivityLockState.AwaitingShortcut, _lock.State);

        _lock.OnShortcut();
        _lock.Poll();

        Assert.Equal(ActivityLockState.Disarmed, _lock.State);
        Assert.Equal(0, _power.LockCount);
    }

    [Fact]
    public void ModifiersReleasedWithoutTheShortcutLock()
    {
        Arm(ActivityResponse.Lock);
        _keyboard.Held = ShortcutModifiers.Control | ShortcutModifiers.Alt;
        Touch();
        _lock.Poll();

        _keyboard.Held = ShortcutModifiers.None;
        _lock.Poll();

        Assert.Equal(1, _power.LockCount);
    }

    [Fact]
    public void ModifiersHeldLongerThanTheShortcutWindowLock()
    {
        Arm(ActivityResponse.Lock);
        _keyboard.Held = ShortcutModifiers.Alt;
        Touch();
        _lock.Poll();

        _clock.Advance(ActivityLock.ShortcutWindow);
        _lock.Poll();

        Assert.Equal(1, _power.LockCount);
    }

    [Fact]
    public void AModifierOutsideTheShortcutGetsNoGrace()
    {
        Arm(ActivityResponse.Lock);

        _keyboard.Held = ShortcutModifiers.Shift;
        Touch();
        _lock.Poll();

        Assert.Equal(1, _power.LockCount);
    }

    [Fact]
    public void TheShortcutArmsTheLockAgainAfterAQuietPeriod()
    {
        Arm(ActivityResponse.Lock);
        _lock.OnShortcut();
        Touch();
        _lock.Poll();
        Assert.Equal(0, _power.LockCount);

        _lock.OnShortcut();
        Assert.Equal(ActivityLockState.Arming, _lock.State);
        Untouched(ActivityLock.QuietPeriod);
        _lock.Poll();
        Touch();
        _lock.Poll();

        Assert.Equal(1, _power.LockCount);
    }

    [Fact]
    public void AWindowsLockDisarmsSoSigningInCannotSuspendThePc()
    {
        Arm(ActivityResponse.Sleep);

        _lock.OnWorkstationLocked();
        Touch();
        _lock.Poll();

        Assert.Equal(ActivityLockState.Disarmed, _lock.State);
        Assert.Equal(0, _power.SuspendCount);
    }

    [Fact]
    public void AFailedLockIsJournaled()
    {
        _power.LockSucceeds = false;
        Arm(ActivityResponse.Lock);

        Touch();
        _lock.Poll();

        Assert.True(_log.Contains("failed"));
    }

    [Fact]
    public void StoppedItIgnoresActivityAndTheShortcut()
    {
        Arm(ActivityResponse.Lock);

        _lock.Stop();
        _lock.OnShortcut();
        Touch();
        _lock.Poll();

        Assert.Equal(ActivityLockState.Off, _lock.State);
        Assert.Equal(0, _power.LockCount);
    }

    private void Arm(ActivityResponse response)
    {
        _lock.Start(response, CtrlAltV);
        Untouched(ActivityLock.QuietPeriod);
        _lock.Poll();
        Assert.Equal(ActivityLockState.Armed, _lock.State);
    }

    private void Untouched(TimeSpan duration)
    {
        _clock.Advance(duration);
        _power.TimeSinceLastInput += duration;
    }

    private void Touch()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        _power.TimeSinceLastInput = TimeSpan.Zero;
    }
}
