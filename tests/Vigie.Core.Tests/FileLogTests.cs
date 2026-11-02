using Vigie.Core.Logging;
using Vigie.Core.Tests.Fakes;
using Xunit;

namespace Vigie.Core.Tests;

public class FileLogTests
{
    private const string LogPath = @"C:\Vigie\logs\vigie.log";

    private readonly FakeFileSystem _fileSystem = new();
    private readonly FileLog _log;

    public FileLogTests() => _log = new FileLog(_fileSystem, new FakeClock(), LogPath);

    [Fact]
    public void WritesOneLinePerEntry()
    {
        _log.Write(LogLevel.Info, "Session démarrée.");
        _log.Write(LogLevel.Error, "Erreur interne :\r\nsur deux lignes");

        Assert.Equal(
            ["2026-01-02 14:00:00 INFO Session démarrée.", "2026-01-02 14:00:00 ERR  Erreur interne : sur deux lignes"],
            _fileSystem.Read(LogPath).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void AFailedWriteDropsTheLineInsteadOfThrowing()
    {
        _fileSystem.FailAppends = true;

        Exception? failure = Record.Exception(() => _log.Write(LogLevel.Info, "Session démarrée."));

        Assert.Null(failure);
        Assert.False(_fileSystem.FileExists(LogPath));
    }
}
