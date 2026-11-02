using System.Globalization;
using Vigie.Core.Platform;

namespace Vigie.Core.Logging;

/// <summary>
/// Appends journal lines to a file and keeps the file from growing without
/// bound: once it passes the size limit it is rotated, and a fixed number of
/// older files are kept.
/// </summary>
public sealed class FileLog : ILog, IDisposable
{
    private static readonly char[] LineBreaks = ['\r', '\n'];

    private readonly Lock _gate = new();
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _keptFiles;

    public FileLog(IFileSystem fileSystem, IClock clock, string path, long maxBytes = 1024 * 1024, int keptFiles = 5)
    {
        _fileSystem = fileSystem;
        _clock = clock;
        _path = path;
        _maxBytes = maxBytes;
        _keptFiles = keptFiles;
    }

    public void Write(LogLevel level, string message)
    {
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{_clock.Now:yyyy-MM-dd HH:mm:ss} {Tag(level)} {Flatten(message)}{Environment.NewLine}");

        lock (_gate)
        {
            RotateIfNeeded();
            _fileSystem.AppendAllText(_path, line);
        }
    }

    public void Dispose()
    {
        // Each write opens and closes the file, so there is nothing to flush.
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERR ",
        _ => "INFO",
    };

    // The journal is one line per entry; a message that spans lines would break
    // that, so breaks become spaces.
    private static string Flatten(string message)
        => message.IndexOfAny(LineBreaks) < 0
            ? message
            : string.Join(' ', message.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void RotateIfNeeded()
    {
        if (!_fileSystem.FileExists(_path) || _fileSystem.GetFileSize(_path) < _maxBytes)
        {
            return;
        }

        string Rotated(int index) => $"{_path}.{index}";

        if (_fileSystem.FileExists(Rotated(_keptFiles)))
        {
            _fileSystem.Delete(Rotated(_keptFiles));
        }

        for (int index = _keptFiles - 1; index >= 1; index--)
        {
            if (_fileSystem.FileExists(Rotated(index)))
            {
                _fileSystem.Move(Rotated(index), Rotated(index + 1), overwrite: true);
            }
        }

        _fileSystem.Move(_path, Rotated(1), overwrite: true);
    }
}
