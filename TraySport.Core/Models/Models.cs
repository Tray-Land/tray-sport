namespace TraySport.Core.Models;

public sealed record Team(
    string Id,
    string Name,
    string Sport,
    string? League,
    string? LeagueId,
    string? BadgeUrl)
{
    public string? ShortName { get; init; }
    public int? FormedYear { get; init; }
    public string? Stadium { get; init; }
    public string? Location { get; init; }
    public string? Country { get; init; }
    public string? Website { get; init; }
    public string? YouTube { get; init; }
    public string? Description { get; init; }
    public string? BannerUrl { get; init; }

    /// <summary>The badge at the CDN's small preview size, for lists and the tray icon.</summary>
    public string? TinyBadgeUrl => ImageUrls.Tiny(BadgeUrl);
}

public enum GameOutcome
{
    Unknown,
    Win,
    Loss,
    Draw,
}

public sealed record Game(
    string Id,
    string HomeTeam,
    string AwayTeam,
    string? HomeTeamId,
    string? AwayTeamId,
    int? HomeScore,
    int? AwayScore,
    DateTimeOffset? StartsAt,
    bool HasStartTime)
{
    public string? League { get; init; }
    public string? LeagueId { get; init; }
    public string? Season { get; init; }
    public string? Sport { get; init; }
    public string? Venue { get; init; }
    public string? Status { get; init; }
    public bool IsPostponed { get; init; }
    public string? VideoUrl { get; init; }
    public string? HomeBadgeUrl { get; init; }
    public string? AwayBadgeUrl { get; init; }

    public bool HasScore => HomeScore is not null && AwayScore is not null;

    public bool IsHome(string teamId) => HomeTeamId == teamId;

    public string OpponentOf(string teamId) => IsHome(teamId) ? AwayTeam : HomeTeam;

    public string? OpponentBadgeOf(string teamId) => ImageUrls.Tiny(IsHome(teamId) ? AwayBadgeUrl : HomeBadgeUrl);
}

public sealed record StandingRow(
    int Rank,
    string TeamId,
    string Team,
    string? BadgeUrl,
    int? Played,
    int? Wins,
    int? Draws,
    int? Losses,
    int? Points)
{
    public string? Group { get; init; }
    public string? Form { get; init; }
}

public sealed record League(string Id, string Name, string Sport, string? CurrentSeason, string? BadgeUrl);

/// <summary>TheSportsDB's CDN serves resized copies at "&lt;url&gt;/tiny", "/small", "/medium".</summary>
public static class ImageUrls
{
    public static string? Tiny(string? url) => Sized(url, "tiny");

    public static string? Small(string? url) => Sized(url, "small");

    private static string? Sized(string? url, string size)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = url.Trim();
        return url.EndsWith("/tiny", StringComparison.Ordinal) || url.EndsWith("/small", StringComparison.Ordinal) || url.EndsWith("/medium", StringComparison.Ordinal)
            ? url
            : $"{url}/{size}";
    }
}
