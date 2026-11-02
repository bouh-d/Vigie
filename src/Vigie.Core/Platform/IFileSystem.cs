namespace Vigie.Core.Platform;

public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    string ReadAllText(string path);

    Stream OpenRead(string path);

    /// <summary>
    /// Writes <paramref name="contents"/> to a sibling temporary file, flushes it
    /// to disk, then replaces <paramref name="path"/> with it. A crash leaves
    /// either the old file or the new one, never a half-written file.
    /// </summary>
    void WriteAllTextAtomic(string path, string contents);

    void AppendAllText(string path, string contents);

    /// <summary>Opens <paramref name="path"/> for appending, creating it and its
    /// directory when needed. The stream is unbuffered: each write reaches the
    /// file before it returns, and closing it never writes.</summary>
    Stream OpenAppend(string path);

    void Delete(string path);

    IReadOnlyList<string> GetFiles(string directory, string searchPattern);

    IReadOnlyList<string> GetDirectories(string directory);

    void DeleteDirectory(string path, bool recursive);

    long GetFileSize(string path);

    void Move(string sourcePath, string destinationPath, bool overwrite);
}
