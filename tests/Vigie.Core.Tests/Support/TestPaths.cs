namespace Vigie.Core.Tests.Support;

/// <summary>
/// Builds absolute paths valid on the operating system running the tests, so
/// path-handling logic can be exercised the same way on Windows and on Linux.
/// </summary>
public static class TestPaths
{
    public static string Root { get; } = OperatingSystem.IsWindows() ? @"C:\" : "/";

    public static string Of(params string[] segments) => Path.Combine([Root, .. segments]);
}
