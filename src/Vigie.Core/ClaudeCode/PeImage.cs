using System.Buffers.Binary;

namespace Vigie.Core.ClaudeCode;

public enum PeSubsystem
{
    WindowsGui = 2,
    WindowsConsole = 3,
}

/// <summary>
/// Reads the subsystem field of a Portable Executable header. Claude Code is a
/// console application; the Claude desktop application, which can also answer
/// to <c>claude</c>, is a graphical one. The file name alone cannot tell them
/// apart.
/// </summary>
public static class PeImage
{
    private const int DosHeaderSize = 64;
    private const int PeHeaderOffsetField = 0x3C;
    private const int SignatureSize = 4;
    private const int CoffHeaderSize = 20;
    private const int SubsystemOffset = 68;
    private const int MaxPeHeaderOffset = 64 * 1024;
    private const ushort Pe32Magic = 0x10B;
    private const ushort Pe32PlusMagic = 0x20B;

    /// <returns>The subsystem value, or null when the stream does not hold a
    /// valid PE image.</returns>
    public static ushort? ReadSubsystem(Stream stream)
    {
        Span<byte> dosHeader = stackalloc byte[DosHeaderSize];
        if (!TryRead(stream, 0, dosHeader) || dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
        {
            return null;
        }

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader[PeHeaderOffsetField..]);
        if (peOffset < DosHeaderSize || peOffset > MaxPeHeaderOffset)
        {
            return null;
        }

        // Signature, COFF header, then the optional header up to and including
        // the two-byte subsystem field. Its offset is the same for PE32 and PE32+.
        Span<byte> header = stackalloc byte[SignatureSize + CoffHeaderSize + SubsystemOffset + sizeof(ushort)];
        if (!TryRead(stream, peOffset, header)
            || header[0] != (byte)'P' || header[1] != (byte)'E' || header[2] != 0 || header[3] != 0)
        {
            return null;
        }

        Span<byte> optionalHeader = header[(SignatureSize + CoffHeaderSize)..];
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(optionalHeader);
        if (magic is not (Pe32Magic or Pe32PlusMagic))
        {
            return null;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(optionalHeader[SubsystemOffset..]);
    }

    private static bool TryRead(Stream stream, long offset, Span<byte> buffer)
    {
        if (offset + buffer.Length > stream.Length)
        {
            return false;
        }

        stream.Position = offset;
        return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }
}
