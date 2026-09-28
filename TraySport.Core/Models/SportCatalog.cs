namespace TraySport.Core.Models;

/// <summary>A sport the app offers, with its tray icon key and the leagues to browse.</summary>
public sealed record SportInfo(string Name, string DisplayName, string IconKey, string Glyph, IReadOnlyList<LeagueInfo> Leagues);

public sealed record LeagueInfo(string Id, string Name);

/// <summary>
/// The sports and leagues offered for browsing. The free API lists only two sports and ten
/// leagues, so the catalog is curated here instead of fetched; any team is still reachable by
/// searching its name.
/// </summary>
public static class SportCatalog
{
    public static IReadOnlyList<SportInfo> Sports { get; } =
    [
        new("American Football", "Football", "football", "\U0001F3C8",
        [
            new("4391", "NFL"),
            new("4479", "NCAA Division 1 Football"),
        ]),
        new("Baseball", "Baseball", "baseball", "⚾",
        [
            new("4424", "MLB"),
        ]),
        new("Basketball", "Basketball", "basketball", "\U0001F3C0",
        [
            new("4387", "NBA"),
            new("4516", "WNBA"),
        ]),
        new("Ice Hockey", "Hockey", "hockey", "\U0001F3D2",
        [
            new("4380", "NHL"),
        ]),
        new("Soccer", "Soccer", "soccer", "⚽",
        [
            new("4328", "English Premier League"),
            new("4346", "American Major League Soccer"),
            new("4335", "Spanish La Liga"),
            new("4331", "German Bundesliga"),
            new("4332", "Italian Serie A"),
            new("4334", "French Ligue 1"),
            new("4480", "UEFA Champions League"),
        ]),
    ];

    /// <summary>The sport whose name matches TheSportsDB's strSport, or null for one we don't list.</summary>
    public static SportInfo? Find(string? sportName) =>
        sportName is null ? null : Sports.FirstOrDefault(s => string.Equals(s.Name, sportName, StringComparison.OrdinalIgnoreCase));

    public static SportInfo? FindByIconKey(string? iconKey) =>
        iconKey is null ? null : Sports.FirstOrDefault(s => string.Equals(s.IconKey, iconKey, StringComparison.OrdinalIgnoreCase));
}
