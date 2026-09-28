using System.Buffers.Binary;

namespace TraySport.Core.Icons;

/// <summary>
/// Writes a .ico file whose images are PNG-compressed (supported since Windows Vista). The app
/// renders a team badge at each tray size and packs them here, so the shell picks the sharpest
/// one for the current DPI.
/// </summary>
public static class IcoWriter
{
    private const int HeaderSize = 6;
    private const int EntrySize = 16;

    /// <param name="images">Square PNGs keyed by pixel size (1–256).</param>
    public static byte[] Write(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        if (images.Count == 0)
        {
            throw new ArgumentException("An icon needs at least one image.", nameof(images));
        }

        int offset = HeaderSize + (EntrySize * images.Count);
        byte[] result = new byte[offset + images.Sum(i => i.Png.Length)];
        Span<byte> span = result;

        BinaryPrimitives.WriteUInt16LittleEndian(span[0..], 0); // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1); // type: icon
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)images.Count);

        for (int i = 0; i < images.Count; i++)
        {
            (int size, byte[] png) = images[i];
            ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);

            Span<byte> entry = span[(HeaderSize + (i * EntrySize))..];
            entry[0] = (byte)(size == 256 ? 0 : size); // width; 0 means 256
            entry[1] = (byte)(size == 256 ? 0 : size); // height
            entry[2] = 0; // palette colors
            entry[3] = 0; // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);  // color planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // bits per pixel
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);

            png.CopyTo(span[offset..]);
            offset += png.Length;
        }

        return result;
    }
}
