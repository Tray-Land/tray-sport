using System.Net.Http;
using TraySport.Core.Api;

namespace TraySport.Services;

/// <summary>
/// The app's one TheSportsDB client. It's shared by every view so its cache and rate-limit gate
/// cover the whole app: reopening the flyout or going back to a team reuses what was fetched.
/// </summary>
internal static class SportsData
{
    private static readonly HttpClient Http = CreateHttpClient();

    public static SportsDbClient Client { get; } = new(Http, Secrets.SportsDbApiKey);

    /// <summary>Downloads an image (a team badge for the tray icon).</summary>
    public static Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken) =>
        Http.GetByteArrayAsync(url, cancellationToken);

    public static string TeamPageUrl(string teamId) => $"https://www.thesportsdb.com/team/{Uri.EscapeDataString(teamId)}";

    private static HttpClient CreateHttpClient()
    {
        HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TraySport/1.0 (+https://www.thesportsdb.com)");
        return http;
    }
}
