using System.Globalization;
using System.Text.Json;
using TraySport.Core.Models;

namespace TraySport.Core.Api;

/// <summary>Parses TheSportsDB JSON into the app's models. Pure, so tests feed it recorded responses.</summary>
public static class SportsDbMapper
{
    public static IReadOnlyList<Team> ParseTeams(string json) =>
        Deserialize(json, SportsDbJsonContext.Default.TeamsResponse)?.Teams?.Select(ToTeam).OfType<Team>().ToList() ?? [];

    /// <summary>Parses either an "events" or a "results" array (eventslast.php uses the latter).</summary>
    public static IReadOnlyList<Game> ParseGames(string json)
    {
        List<EventDto>? events = Deserialize(json, SportsDbJsonContext.Default.EventsResponse)?.Events
            ?? Deserialize(json, SportsDbJsonContext.Default.ResultsResponse)?.Results;
        return events?.Select(ToGame).OfType<Game>().ToList() ?? [];
    }

    public static IReadOnlyList<StandingRow> ParseTable(string json) =>
        Deserialize(json, SportsDbJsonContext.Default.TableResponse)?.Table?.Select(ToRow).OfType<StandingRow>().OrderBy(r => r.Rank).ToList() ?? [];

    public static IReadOnlyList<League> ParseLeagues(string json) =>
        Deserialize(json, SportsDbJsonContext.Default.LeaguesResponse)?.Leagues?
            .Where(l => !string.IsNullOrWhiteSpace(l.IdLeague) && !string.IsNullOrWhiteSpace(l.StrLeague))
            .Select(l => new League(l.IdLeague!, l.StrLeague!, l.StrSport ?? "", Clean(l.StrCurrentSeason), Clean(l.StrBadge)))
            .ToList() ?? [];

    /// <summary>
    /// TheSportsDB timestamps are UTC. strTimestamp is preferred; dateEvent + strTime is the
    /// fallback, and a date alone means the start time isn't known yet.
    /// </summary>
    public static (DateTimeOffset? StartsAt, bool HasTime) ParseStart(string? timestamp, string? date, string? time)
    {
        const DateTimeStyles Utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (!string.IsNullOrWhiteSpace(timestamp) &&
            DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, Utc, out DateTimeOffset ts))
        {
            return (ts, true);
        }

        if (string.IsNullOrWhiteSpace(date) ||
            !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
        {
            return (null, false);
        }

        if (!string.IsNullOrWhiteSpace(time) &&
            TimeSpan.TryParse(time.Split('+')[0], CultureInfo.InvariantCulture, out TimeSpan clock))
        {
            return (new DateTimeOffset(day.Add(clock), TimeSpan.Zero), true);
        }

        return (new DateTimeOffset(day, TimeSpan.Zero), false);
    }

    /// <summary>Links in the API often lack a scheme ("www.arsenal.com"); returns an absolute https URL or null.</summary>
    public static string? NormalizeUrl(string? url)
    {
        url = Clean(url);
        if (url is null)
        {
            return null;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url.TrimStart('/');
        }

        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Host.Contains('.') ? uri.AbsoluteUri : null;
    }

    private static Team? ToTeam(TeamDto dto)
    {
        if (Clean(dto.IdTeam) is not { } id || Clean(dto.StrTeam) is not { } name)
        {
            return null;
        }

        return new Team(id, name, Clean(dto.StrSport) ?? "", Clean(dto.StrLeague), Clean(dto.IdLeague), Clean(dto.StrBadge))
        {
            ShortName = Clean(dto.StrTeamShort),
            FormedYear = ParseInt(dto.IntFormedYear),
            Stadium = Clean(dto.StrStadium),
            Location = Clean(dto.StrLocation),
            Country = Clean(dto.StrCountry),
            Website = NormalizeUrl(dto.StrWebsite),
            YouTube = NormalizeUrl(dto.StrYoutube),
            Description = Clean(dto.StrDescriptionEN),
            BannerUrl = Clean(dto.StrBanner),
        };
    }

    private static Game? ToGame(EventDto dto)
    {
        if (Clean(dto.IdEvent) is not { } id)
        {
            return null;
        }

        (DateTimeOffset? startsAt, bool hasTime) = ParseStart(dto.StrTimestamp, dto.DateEvent, dto.StrTime);
        return new Game(
            id,
            Clean(dto.StrHomeTeam) ?? "Home",
            Clean(dto.StrAwayTeam) ?? "Away",
            Clean(dto.IdHomeTeam),
            Clean(dto.IdAwayTeam),
            ParseInt(dto.IntHomeScore),
            ParseInt(dto.IntAwayScore),
            startsAt,
            hasTime)
        {
            League = Clean(dto.StrLeague),
            LeagueId = Clean(dto.IdLeague),
            Season = Clean(dto.StrSeason),
            Sport = Clean(dto.StrSport),
            Venue = Clean(dto.StrVenue),
            Status = Clean(dto.StrStatus),
            IsPostponed = string.Equals(Clean(dto.StrPostponed), "yes", StringComparison.OrdinalIgnoreCase),
            VideoUrl = NormalizeUrl(dto.StrVideo),
            HomeBadgeUrl = Clean(dto.StrHomeTeamBadge),
            AwayBadgeUrl = Clean(dto.StrAwayTeamBadge),
        };
    }

    private static StandingRow? ToRow(TableRowDto dto)
    {
        if (ParseInt(dto.IntRank) is not { } rank || Clean(dto.IdTeam) is not { } id)
        {
            return null;
        }

        return new StandingRow(rank, id, Clean(dto.StrTeam) ?? "", Clean(dto.StrBadge),
            ParseInt(dto.IntPlayed), ParseInt(dto.IntWin), ParseInt(dto.IntDraw), ParseInt(dto.IntLoss), ParseInt(dto.IntPoints))
        {
            Group = Clean(dto.StrGroup),
            Form = Clean(dto.StrForm),
        };
    }

    private static T? Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
