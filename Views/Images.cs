using Microsoft.UI.Xaml.Media.Imaging;

namespace TraySport.Views;

internal static class Images
{
    /// <summary>
    /// A badge decoded at the size it's drawn (Logical scales it by the display's DPI), so a list of logos
    /// doesn't hold full-size bitmaps in memory.
    /// </summary>
    public static BitmapImage? Badge(string? url, int displaySize)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        return new BitmapImage
        {
            DecodePixelWidth = displaySize,
            DecodePixelType = DecodePixelType.Logical,
            UriSource = uri,
        };
    }
}
