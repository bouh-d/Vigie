using Vigie.Core.ClaudeCode;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

public class PeImageTests
{
    [Fact]
    public void ReadsConsoleSubsystem()
        => Assert.Equal((ushort)PeSubsystem.WindowsConsole, Read(PeImageBuilder.Console()));

    [Fact]
    public void ReadsGuiSubsystem()
        => Assert.Equal((ushort)PeSubsystem.WindowsGui, Read(PeImageBuilder.Gui()));

    [Fact]
    public void ReadsConsoleSubsystemForPe32()
        => Assert.Equal((ushort)PeSubsystem.WindowsConsole, Read(PeImageBuilder.Build(3, magic: 0x10B)));

    [Fact]
    public void RejectsFileWithoutMzSignature()
    {
        byte[] bytes = PeImageBuilder.Console();
        bytes[0] = 0;
        Assert.Null(Read(bytes));
    }

    [Fact]
    public void RejectsTruncatedFile()
        => Assert.Null(Read([0x4D, 0x5A, 0x00, 0x00]));

    private static ushort? Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return PeImage.ReadSubsystem(stream);
    }
}
