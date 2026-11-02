using System.Buffers.Binary;

namespace Vigie.Core.Tests.Support;

/// <summary>
/// Builds the smallest byte sequence that carries a valid DOS stub, PE
/// signature and optional header subsystem field, so the subsystem reader can
/// be exercised without shipping real binaries.
/// </summary>
public static class PeImageBuilder
{
    public static byte[] Build(ushort subsystem, ushort magic = 0x20B)
    {
        const int peOffset = 128;
        byte[] image = new byte[peOffset + 4 + 20 + 70];

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), peOffset);

        image[peOffset] = (byte)'P';
        image[peOffset + 1] = (byte)'E';

        int optionalHeader = peOffset + 4 + 20;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optionalHeader), magic);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optionalHeader + 68), subsystem);

        return image;
    }

    public static byte[] Console() => Build(3);

    public static byte[] Gui() => Build(2);
}
