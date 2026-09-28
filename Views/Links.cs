using Windows.System;

namespace TraySport.Views;

internal static class Links
{
    /// <summary>Opens a web link (YouTube, a team site) in the default browser or app.</summary>
    public static void Open(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            _ = Launcher.LaunchUriAsync(uri);
        }
    }
}
