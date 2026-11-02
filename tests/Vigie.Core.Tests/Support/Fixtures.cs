namespace Vigie.Core.Tests.Support;

public static class Fixtures
{
    public static IReadOnlyList<string> ReadLines(string relativePath)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllLines(path);
    }
}
