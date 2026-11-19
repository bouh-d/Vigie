using Vigie.Core.Platform;
using Vigie.Core.Session;
using Vigie.Core.Storage;
using Vigie.Core.Tests.Fakes;
using Xunit;

namespace Vigie.Core.Tests;

public class UserSettingsStoreTests
{
    private const string SettingsPath = @"C:\Vigie\settings.json";
    private const int VkF9 = 0x78;

    private readonly FakeFileSystem _fileSystem = new();

    [Fact]
    public void ChoicesSurviveASaveAndALoad()
    {
        var store = new UserSettingsStore(_fileSystem, SettingsPath);
        var settings = new UserSettings
        {
            DurationMinutes = 45,
            UntilTask = true,
            BatteryThresholdPercent = 30,
            ActivityLockEnabled = true,
            ActivityResponse = ActivityResponse.Sleep,
            ActivityShortcut = new Shortcut(ShortcutModifiers.Control | ShortcutModifiers.Shift, VkF9),
            ResumeAfterUsageLimit = true,
            UseBatteryToTheEnd = true,
            HomeLab = true,
        };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void AMissingOrDamagedFileGivesTheDefaults()
    {
        var store = new UserSettingsStore(_fileSystem, SettingsPath);
        Assert.Equal(new UserSettings(), store.Load());
        Assert.False(store.Load().ResumeAfterUsageLimit);
        Assert.False(store.Load().UseBatteryToTheEnd);

        _fileSystem.Write(SettingsPath, "{ \"durationMinutes\": ");

        Assert.Equal(new UserSettings(), store.Load());
    }

    [Fact]
    public void AFileFromVersion011KeepsTheBatteryStopAndTheUsualDuration()
    {
        // Written by Vigie 0.1.1, before the battery and home lab options.
        _fileSystem.Write(SettingsPath, """
            {
              "durationMinutes": 1440,
              "untilTask": false,
              "taskCapHours": 8,
              "workWithLidClosed": true,
              "keepDisplayOn": true,
              "lockOnLidClose": true,
              "suspendAtEnd": true,
              "batteryThresholdPercent": 20,
              "modernStandbyCompatibility": false,
              "activityLockEnabled": false,
              "activityResponse": "Lock",
              "activityShortcut": { "modifiers": "Alt, Control", "virtualKey": 86 },
              "resumeAfterUsageLimit": false
            }
            """);

        UserSettings settings = new UserSettingsStore(_fileSystem, SettingsPath).Load();

        Assert.False(settings.UseBatteryToTheEnd);
        Assert.False(settings.HomeLab);
        Assert.Null(settings.ClaudeFolder);
        Assert.Equal(1440, settings.DurationMinutes);
        Assert.True(settings.KeepDisplayOn);
    }

    [Fact]
    public void AValueOutOfRangeFallsBackToItsDefaultAndKeepsTheOthers()
    {
        _fileSystem.Write(SettingsPath, """
            {
              "durationMinutes": 2,
              "batteryThresholdPercent": 90,
              "keepDisplayOn": true,
              "activityShortcut": { "modifiers": "Shift", "virtualKey": 86 }
            }
            """);

        UserSettings settings = new UserSettingsStore(_fileSystem, SettingsPath).Load();

        Assert.Equal(120, settings.DurationMinutes);
        Assert.Equal(20, settings.BatteryThresholdPercent);
        Assert.Equal(Shortcut.Default, settings.ActivityShortcut);
        Assert.True(settings.KeepDisplayOn);
    }
}
