namespace TraySport.Core.Icons;

public static class IconPixels
{
    /// <summary>Fits a width×height image into the smallest square that holds it, returning its scaled size.</summary>
    public static (int Width, int Height) FitInSquare(int width, int height, int size)
    {
        if (width <= 0 || height <= 0)
        {
            return (size, size);
        }

        double scale = Math.Min((double)size / width, (double)size / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// Centers a 32-bit pixel buffer (width×height) in a transparent size×size buffer, so a
    /// non-square badge keeps its proportions in the square tray icon.
    /// </summary>
    public static byte[] CenterInSquare(ReadOnlySpan<byte> pixels, int width, int height, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, width * height * 4, nameof(pixels));
        byte[] result = new byte[size * size * 4];
        int left = (size - width) / 2;
        int top = (size - height) / 2;
        for (int y = 0; y < height; y++)
        {
            pixels.Slice(y * width * 4, width * 4).CopyTo(result.AsSpan((((top + y) * size) + left) * 4));
        }

        return result;
    }
}
