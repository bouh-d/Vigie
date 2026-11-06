using Vigie.Core.Storage;
using Vigie.Core.Tests.Fakes;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

public class PackageRedirectionTests
{
    private static readonly string LocalAppData = TestPaths.Of("Users", "dev", "AppData", "Local");
    private static readonly string VigieRoot = Path.Combine(LocalAppData, "Vigie");
    private static readonly string ClaudeStorage = Path.Combine(LocalAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Local", "Vigie");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 19, 30, 0, TimeSpan.Zero);

    private readonly FakeFileSystem _fileSystem = new();

    [Fact]
    public void WritesThatLandInPlaceAreNotRedirected()
    {
        _fileSystem.Write(Path.Combine(LocalAppData, "Packages", "Other_123", "Settings", "settings.dat"), "x");

        Assert.Null(PackageRedirection.Detect(_fileSystem, LocalAppData));
        Assert.DoesNotContain(_fileSystem.Paths, p => p.Contains("redirect-probe", StringComparison.Ordinal));
    }

    [Fact]
    public void WritesKeptInAPackageNameThatPackage()
    {
        RedirectIntoClaude();

        Assert.Equal("Claude_pzs8sxrjxfjjc", PackageRedirection.Detect(_fileSystem, LocalAppData));
        Assert.DoesNotContain(_fileSystem.Paths, p => p.Contains("redirect-probe", StringComparison.Ordinal));
    }

    [Fact]
    public void FilesLeftInAPackageByAnEarlierStartAreNotARedirection()
    {
        _fileSystem.Write(Path.Combine(ClaudeStorage, "settings.json"), "{}");

        Assert.Null(PackageRedirection.Detect(_fileSystem, LocalAppData));
    }

    [Fact]
    public void ARedirectedStartRelaunchesOnceAndThenSaysSo()
    {
        Assert.Equal(StartPlacement.Run, PackageRedirection.Decide(null, lastRelaunch: null, Now));
        Assert.Equal(StartPlacement.Relaunch, PackageRedirection.Decide("Claude_pzs8sxrjxfjjc", lastRelaunch: null, Now));
        Assert.Equal(StartPlacement.Refuse, PackageRedirection.Decide("Claude_pzs8sxrjxfjjc", Now.AddSeconds(-5), Now));
        Assert.Equal(StartPlacement.Relaunch, PackageRedirection.Decide("Claude_pzs8sxrjxfjjc", Now - PackageRedirection.RelaunchWindow, Now));
    }

    [Fact]
    public void TheRelaunchMarkerIsReadBackInsideThePackage()
    {
        RedirectIntoClaude();
        Assert.Null(PackageRedirection.ReadLastRelaunch(_fileSystem, LocalAppData));

        PackageRedirection.RecordRelaunch(_fileSystem, Now, LocalAppData);

        Assert.Equal(Now, PackageRedirection.ReadLastRelaunch(_fileSystem, LocalAppData));
        Assert.True(_fileSystem.Has(Path.Combine(ClaudeStorage, "relaunch.txt")));
    }

    private void RedirectIntoClaude()
        => _fileSystem.Redirect = path => path.StartsWith(VigieRoot, StringComparison.OrdinalIgnoreCase)
            ? ClaudeStorage + path[VigieRoot.Length..]
            : path;
}
