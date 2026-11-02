using System.Globalization;
using Vigie.Core.Platform;

namespace Vigie.Core.Storage;

/// <summary>
/// The locations Vigie writes to, all under <c>%LOCALAPPDATA%\Vigie</c>, and
/// the retention of recorded runs.
/// </summary>
public sealed class VigieStorage(IEnvironment environment, IFileSystem fileSystem)
{
    private const string RunDirectoryFormat = "yyyyMMdd-HHmmss";

    public string Root { get; } = Path.Combine(environment.LocalApplicationData, "Vigie");

    public string RestoreJournalPath => Path.Combine(Root, "restore.json");

    public string LogDirectory => Path.Combine(Root, "logs");

    public string LogFilePath => Path.Combine(LogDirectory, "vigie.log");

    public string RunsDirectory => Path.Combine(Root, "runs");

    public void EnsureCreated() => fileSystem.CreateDirectory(Root);

    public string CreateRunDirectory(DateTimeOffset now)
    {
        string stamp = now.ToString(RunDirectoryFormat, CultureInfo.InvariantCulture);
        string path = Path.Combine(RunsDirectory, stamp);

        // A second run started in the same second gets a suffix.
        int suffix = 2;
        while (fileSystem.DirectoryExists(path))
        {
            path = Path.Combine(RunsDirectory, $"{stamp}-{suffix}");
            suffix++;
        }

        fileSystem.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes recorded runs older than <paramref name="maxAge"/>,
    /// judged by the timestamp in the directory name.</summary>
    public int PruneRuns(TimeSpan maxAge, DateTimeOffset now)
    {
        int removed = 0;
        foreach (string directory in fileSystem.GetDirectories(RunsDirectory))
        {
            // Both "20260102-150405" and its "-2" disambiguated form start with
            // the fixed-width timestamp.
            string name = Path.GetFileName(directory);
            if (name.Length < RunDirectoryFormat.Length)
            {
                continue;
            }

            string stamp = name[..RunDirectoryFormat.Length];
            if (DateTimeOffset.TryParseExact(stamp, RunDirectoryFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset created)
                && now - created > maxAge)
            {
                fileSystem.DeleteDirectory(directory, recursive: true);
                removed++;
            }
        }

        return removed;
    }
}
