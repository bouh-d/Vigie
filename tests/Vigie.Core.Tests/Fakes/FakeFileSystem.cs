using System.Text;
using Vigie.Core.Platform;

namespace Vigie.Core.Tests.Fakes;

/// <summary>
/// An in-memory file system. Atomic writes go through a visible temporary file
/// so a test can simulate a crash between the write and the rename.
/// </summary>
public sealed class FakeFileSystem : IFileSystem
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, runs after the temporary file is written but before
    /// the rename, then clears itself.</summary>
    public Action? CrashDuringAtomicWrite { get; set; }

    public void Write(string path, string contents) => _files[path] = Utf8.GetBytes(contents);

    public void WriteBytes(string path, byte[] contents) => _files[path] = contents;

    public bool Has(string path) => _files.ContainsKey(path);

    public string Read(string path) => Utf8.GetString(_files[path]);

    public IReadOnlyCollection<string> Paths => _files.Keys;

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool DirectoryExists(string path)
        => _files.Keys.Any(p => p.StartsWith(WithSeparator(path), StringComparison.OrdinalIgnoreCase));

    public void CreateDirectory(string path)
    {
    }

    public string ReadAllText(string path) => Read(path);

    public Stream OpenRead(string path)
        => _files.TryGetValue(path, out byte[]? bytes)
            ? new MemoryStream(bytes, writable: false)
            : throw new FileNotFoundException(path);

    public void WriteAllTextAtomic(string path, string contents)
    {
        string temporaryPath = path + ".tmp";
        _files[temporaryPath] = Utf8.GetBytes(contents);

        if (CrashDuringAtomicWrite is { } crash)
        {
            CrashDuringAtomicWrite = null;
            crash();
            return;
        }

        _files[path] = _files[temporaryPath];
        _files.Remove(temporaryPath);
    }

    public void AppendAllText(string path, string contents)
    {
        byte[] appended = Utf8.GetBytes(contents);
        _files[path] = _files.TryGetValue(path, out byte[]? existing) ? [.. existing, .. appended] : appended;
    }

    public Stream OpenAppend(string path)
    {
        var stream = new AppendStream(this, path);
        return stream;
    }

    public void Delete(string path) => _files.Remove(path);

    public IReadOnlyList<string> GetDirectories(string directory)
    {
        string prefix = WithSeparator(directory);
        return _files.Keys
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.Combine(directory, p[prefix.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        string prefix = WithSeparator(path);
        foreach (string key in _files.Keys.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _files.Remove(key);
        }
    }

    public long GetFileSize(string path) => _files[path].Length;

    public void Move(string sourcePath, string destinationPath, bool overwrite)
    {
        _files[destinationPath] = _files[sourcePath];
        _files.Remove(sourcePath);
    }

    private static string WithSeparator(string directory)
        => directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;

    private sealed class AppendStream(FakeFileSystem owner, string path) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            owner._files[path] = owner._files.TryGetValue(path, out byte[]? existing)
                ? [.. existing, .. buffer]
                : buffer.ToArray();
        }

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));
    }
}
