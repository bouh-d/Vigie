using System.Globalization;
using Vigie.Core.Platform;

namespace Vigie.Core.Storage;

public enum StartPlacement
{
    /// <summary>Writes land where they should: start normally.</summary>
    Run,

    /// <summary>Writes are redirected: start again through Explorer.</summary>
    Relaunch,

    /// <summary>Writes are still redirected right after a relaunch: say so
    /// rather than relaunch again.</summary>
    Refuse,
}

/// <summary>
/// A process started from inside a packaged app, such as the Claude desktop
/// app, can have its writes under <c>%LOCALAPPDATA%</c> and <c>HKCU</c> kept in
/// that package's private storage. The restore journal and the sign-in entry
/// would then be out of reach of a later Vigie and of Windows itself. Such a
/// process has no package identity of its own, so the redirection is observed
/// directly: a probe file written under <c>%LOCALAPPDATA%\Vigie</c> that shows
/// up in a package's <c>LocalCache</c>.
/// </summary>
public static class PackageRedirection
{
    private const string MarkerName = "relaunch.txt";

    /// <summary>How recent a relaunch has to be for a redirected start to be
    /// taken as that relaunch having failed. Explorer starts the new instance
    /// within a few seconds.</summary>
    public static TimeSpan RelaunchWindow { get; } = TimeSpan.FromSeconds(15);

    /// <returns>The package whose storage receives the writes, or null when they
    /// land under <paramref name="localAppData"/> as they should.</returns>
    public static string? Detect(IFileSystem fileSystem, string localAppData, string folder = "Vigie")
    {
        string probeName = $".redirect-probe-{Guid.NewGuid():N}";
        string probePath = Path.Combine(localAppData, folder, probeName);
        try
        {
            fileSystem.CreateDirectory(Path.Combine(localAppData, folder));
            fileSystem.AppendAllText(probePath, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing can be learned without the probe; Vigie starts as usual
            // and reports any problem writing its files the usual way.
            return null;
        }

        try
        {
            string packages = Path.Combine(localAppData, "Packages");
            if (!fileSystem.DirectoryExists(packages))
            {
                return null;
            }

            foreach (string package in fileSystem.GetDirectories(packages))
            {
                if (fileSystem.FileExists(Path.Combine(package, "LocalCache", "Local", folder, probeName)))
                {
                    return Path.GetFileName(package);
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                fileSystem.Delete(probePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover empty probe file is harmless.
            }
        }
    }

    public static StartPlacement Decide(string? redirectingPackage, DateTimeOffset? lastRelaunch, DateTimeOffset now)
    {
        if (redirectingPackage is null)
        {
            return StartPlacement.Run;
        }

        return lastRelaunch is DateTimeOffset at && now >= at && now - at < RelaunchWindow
            ? StartPlacement.Refuse
            : StartPlacement.Relaunch;
    }

    /// <summary>When the last relaunch was attempted, as seen from inside the
    /// package: the marker is redirected like every other write.</summary>
    public static DateTimeOffset? ReadLastRelaunch(IFileSystem fileSystem, string localAppData, string folder = "Vigie")
    {
        string marker = Path.Combine(localAppData, folder, MarkerName);
        try
        {
            return fileSystem.FileExists(marker)
                && DateTimeOffset.TryParse(fileSystem.ReadAllText(marker).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at)
                    ? at
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void RecordRelaunch(IFileSystem fileSystem, DateTimeOffset now, string localAppData, string folder = "Vigie")
        => fileSystem.WriteAllTextAtomic(
            Path.Combine(localAppData, folder, MarkerName),
            now.ToString("O", CultureInfo.InvariantCulture));
}
