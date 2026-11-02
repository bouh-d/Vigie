using System.Diagnostics;
namespace Vigie.Core.Platform;

public sealed class SystemEnvironment : IEnvironment
{
    public string LocalApplicationData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public int CurrentProcessId => Environment.ProcessId;

    public string? CurrentProcessPath => Environment.ProcessPath;

    public DateTimeOffset CurrentProcessStartTimeUtc => Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public string CurrentDirectory => Environment.CurrentDirectory;

    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);
}
