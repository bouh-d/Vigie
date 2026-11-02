namespace Vigie.Core.Platform;

public interface IEnvironment
{
    string LocalApplicationData { get; }

    string UserProfile { get; }

    int CurrentProcessId { get; }

    string? CurrentProcessPath { get; }

    DateTimeOffset CurrentProcessStartTimeUtc { get; }

    string CurrentDirectory { get; }

    string? GetVariable(string name);
}
