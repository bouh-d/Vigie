using Xunit;

namespace Vigie.Core.Tests.Support;

/// <summary>A theory that needs the Windows system libraries, skipped
/// elsewhere.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs the Windows system libraries.";
        }
    }
}
