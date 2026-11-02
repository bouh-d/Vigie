using System.Text;

namespace Vigie.Core.Platform;

public sealed class SystemFileSystem : IFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string ReadAllText(string path) => File.ReadAllText(path, Utf8NoBom);

    public Stream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public void WriteAllTextAtomic(string path, string contents)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A rooted path is required.", nameof(path));
        Directory.CreateDirectory(directory);

        // A fixed name means an interrupted write leaves one stale file that the
        // next write overwrites, rather than a new stray file each time.
        string temporaryPath = path + ".tmp";
        byte[] bytes = Utf8NoBom.GetBytes(contents);

        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    public void AppendAllText(string path, string contents)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(path, contents, Utf8NoBom);
    }

    public Stream OpenAppend(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 0);
    }

    public void Delete(string path) => File.Delete(path);

    public IReadOnlyList<string> GetFiles(string directory, string searchPattern)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, searchPattern)
            : [];
    }

    public IReadOnlyList<string> GetDirectories(string directory)
        => Directory.Exists(directory) ? Directory.GetDirectories(directory) : [];

    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public void Move(string sourcePath, string destinationPath, bool overwrite)
        => File.Move(sourcePath, destinationPath, overwrite);
}
