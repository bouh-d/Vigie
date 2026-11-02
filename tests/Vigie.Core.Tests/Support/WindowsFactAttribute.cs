using Xunit;

namespace Vigie.Core.Tests.Support;

/// <summary>A test that needs Windows itself, skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs Windows.";
        }
    }
}
