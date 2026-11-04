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
        };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void AMissingOrDamagedFileGivesTheDefaults()
    {
        var store = new UserSettingsStore(_fileSystem, SettingsPath);
        Assert.Equal(new UserSettings(), store.Load());

        _fileSystem.Write(SettingsPath, "{ \"durationMinutes\": ");

        Assert.Equal(new UserSettings(), store.Load());
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
