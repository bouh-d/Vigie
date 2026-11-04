using System.Text.Json;
using System.Text.Json.Serialization;
using Vigie.Core.Platform;
using Vigie.Core.Session;

namespace Vigie.Core.Storage;

/// <summary>The choices made in the window, kept from one launch to the next.</summary>
public sealed record UserSettings
{
    /// <summary>The fixed duration, or the custom one when the session follows a task.</summary>
    public int DurationMinutes { get; init; } = 120;

    public bool UntilTask { get; init; }

    public int TaskCapHours { get; init; } = 8;

    public bool WorkWithLidClosed { get; init; } = true;

    public bool KeepDisplayOn { get; init; }

    public bool LockOnLidClose { get; init; } = true;

    public bool SuspendAtEnd { get; init; } = true;

    public int BatteryThresholdPercent { get; init; } = 20;

    public bool ModernStandbyCompatibility { get; init; }

    public bool ActivityLockEnabled { get; init; }

    public ActivityResponse ActivityResponse { get; init; } = ActivityResponse.Lock;

    public Shortcut ActivityShortcut { get; init; } = Shortcut.Default;

    /// <summary>A value out of range, from an edited or older file, falls back
    /// to its default; the other choices are kept.</summary>
    public UserSettings Sanitized()
    {
        var defaults = new UserSettings();
        TimeSpan duration = TimeSpan.FromMinutes(DurationMinutes);
        return this with
        {
            DurationMinutes = duration >= SessionOptions.MinimumDuration && duration <= SessionOptions.MaximumDuration ? DurationMinutes : defaults.DurationMinutes,
            TaskCapHours = TaskCapHours is >= 1 and <= 24 ? TaskCapHours : defaults.TaskCapHours,
            BatteryThresholdPercent = BatteryThresholdPercent is >= 10 and <= 50 ? BatteryThresholdPercent : defaults.BatteryThresholdPercent,
            ActivityResponse = Enum.IsDefined(ActivityResponse) ? ActivityResponse : defaults.ActivityResponse,
            ActivityShortcut = ActivityShortcut.IsValid() ? ActivityShortcut : defaults.ActivityShortcut,
        };
    }
}

public sealed class UserSettingsStore(IFileSystem fileSystem, string filePath)
{
    public UserSettings Load()
    {
        try
        {
            if (!fileSystem.FileExists(filePath))
            {
                return new UserSettings();
            }

            UserSettings? settings = JsonSerializer.Deserialize(fileSystem.ReadAllText(filePath), UserSettingsJsonContext.Default.UserSettings);
            return settings?.Sanitized() ?? new UserSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or unreadable file costs the user their choices, nothing more.
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
        => fileSystem.WriteAllTextAtomic(filePath, JsonSerializer.Serialize(settings, UserSettingsJsonContext.Default.UserSettings));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(UserSettings))]
internal sealed partial class UserSettingsJsonContext : JsonSerializerContext;
