using Vigie.Core.Logging;
using Vigie.Core.Platform;
using Vigie.Core.Power;

namespace Vigie.Core.Tests.Fakes;

public sealed class FakeEnvironment : IEnvironment
{
    public string LocalApplicationData { get; set; } = @"C:\Users\dev\AppData\Local";

    public string UserProfile { get; set; } = @"C:\Users\dev";

    public int CurrentProcessId { get; set; } = 4242;

    public string? CurrentProcessPath { get; set; } = @"C:\Program Files\Vigie\Vigie.exe";

    public DateTimeOffset CurrentProcessStartTimeUtc { get; set; } = new(2026, 1, 2, 13, 0, 0, TimeSpan.Zero);

    public string CurrentDirectory { get; set; } = @"C:\Users\dev\projects";

    public Dictionary<string, string?> Variables { get; } = [];

    public string? GetVariable(string name) => Variables.GetValueOrDefault(name);
}

public sealed class FakeRegistry : IRegistry
{
    public Dictionary<(string Key, string Value), int> MachineInts { get; } = [];

    public HashSet<string> MachineSubKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<(string Key, string Value), string> CurrentUserStrings { get; } = [];

    public bool TryReadMachineInt32(string subKeyPath, string valueName, out int value)
        => MachineInts.TryGetValue((subKeyPath, valueName), out value);

    public bool MachineSubKeyExists(string subKeyPath) => MachineSubKeys.Contains(subKeyPath);

    public void WriteCurrentUserString(string subKeyPath, string valueName, string value)
        => CurrentUserStrings[(subKeyPath, valueName)] = value;

    public void DeleteCurrentUserValue(string subKeyPath, string valueName)
        => CurrentUserStrings.Remove((subKeyPath, valueName));
}

public sealed class RecordingLog : ILog
{
    public List<LogEntry> Entries { get; } = [];

    public void Write(LogLevel level, string message) => Entries.Add(new LogEntry(DateTimeOffset.Now, level, message));

    public bool Contains(string fragment) => Entries.Exists(e => e.Message.Contains(fragment, StringComparison.Ordinal));
}

public sealed class FakeCrashRecovery : ICrashRecovery
{
    public int ArmCount { get; private set; }

    public int DisarmCount { get; private set; }

    public bool? LastKeepStartupEntry { get; private set; }

    public void Arm(SessionOwner owner) => ArmCount++;

    public void Disarm(bool keepStartupEntry)
    {
        DisarmCount++;
        LastKeepStartupEntry = keepStartupEntry;
    }
}
