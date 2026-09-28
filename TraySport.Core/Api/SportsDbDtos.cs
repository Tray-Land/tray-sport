using System.Text.Json.Serialization;

namespace TraySport.Core.Api;

// TheSportsDB v1 returns every value as a string (or null), and an empty result as a null array.
// These DTOs mirror the wire format; SportsDbMapper turns them into the app's models.

internal sealed class TeamsResponse
{
    [JsonPropertyName("teams")]
    public List<TeamDto>? Teams { get; set; }
}

internal sealed class TeamDto
{
    [JsonPropertyName("idTeam")] public string? IdTeam { get; set; }
    [JsonPropertyName("strTeam")] public string? StrTeam { get; set; }
    [JsonPropertyName("strTeamShort")] public string? StrTeamShort { get; set; }
    [JsonPropertyName("strTeamAlternate")] public string? StrTeamAlternate { get; set; }
    [JsonPropertyName("intFormedYear")] public string? IntFormedYear { get; set; }
    [JsonPropertyName("strSport")] public string? StrSport { get; set; }
    [JsonPropertyName("strLeague")] public string? StrLeague { get; set; }
    [JsonPropertyName("idLeague")] public string? IdLeague { get; set; }
    [JsonPropertyName("strStadium")] public string? StrStadium { get; set; }
    [JsonPropertyName("strLocation")] public string? StrLocation { get; set; }
    [JsonPropertyName("strCountry")] public string? StrCountry { get; set; }
    [JsonPropertyName("strWebsite")] public string? StrWebsite { get; set; }
    [JsonPropertyName("strYoutube")] public string? StrYoutube { get; set; }
    [JsonPropertyName("strDescriptionEN")] public string? StrDescriptionEN { get; set; }
    [JsonPropertyName("strColour1")] public string? StrColour1 { get; set; }
    [JsonPropertyName("strBadge")] public string? StrBadge { get; set; }
    [JsonPropertyName("strBanner")] public string? StrBanner { get; set; }
    [JsonPropertyName("strFanart1")] public string? StrFanart1 { get; set; }
}

internal sealed class EventsResponse
{
    [JsonPropertyName("events")]
    public List<EventDto>? Events { get; set; }
}

/// <summary>eventslast.php names its array "results" instead of "events".</summary>
internal sealed class ResultsResponse
{
    [JsonPropertyName("results")]
    public List<EventDto>? Results { get; set; }
}

internal sealed class EventDto
{
    [JsonPropertyName("idEvent")] public string? IdEvent { get; set; }
    [JsonPropertyName("strEvent")] public string? StrEvent { get; set; }
    [JsonPropertyName("strSport")] public string? StrSport { get; set; }
    [JsonPropertyName("idLeague")] public string? IdLeague { get; set; }
    [JsonPropertyName("strLeague")] public string? StrLeague { get; set; }
    [JsonPropertyName("strSeason")] public string? StrSeason { get; set; }
    [JsonPropertyName("intRound")] public string? IntRound { get; set; }
    [JsonPropertyName("strHomeTeam")] public string? StrHomeTeam { get; set; }
    [JsonPropertyName("strAwayTeam")] public string? StrAwayTeam { get; set; }
    [JsonPropertyName("idHomeTeam")] public string? IdHomeTeam { get; set; }
    [JsonPropertyName("idAwayTeam")] public string? IdAwayTeam { get; set; }
    [JsonPropertyName("strHomeTeamBadge")] public string? StrHomeTeamBadge { get; set; }
    [JsonPropertyName("strAwayTeamBadge")] public string? StrAwayTeamBadge { get; set; }
    [JsonPropertyName("intHomeScore")] public string? IntHomeScore { get; set; }
    [JsonPropertyName("intAwayScore")] public string? IntAwayScore { get; set; }
    [JsonPropertyName("strTimestamp")] public string? StrTimestamp { get; set; }
    [JsonPropertyName("dateEvent")] public string? DateEvent { get; set; }
    [JsonPropertyName("strTime")] public string? StrTime { get; set; }
    [JsonPropertyName("strVenue")] public string? StrVenue { get; set; }
    [JsonPropertyName("strStatus")] public string? StrStatus { get; set; }
    [JsonPropertyName("strPostponed")] public string? StrPostponed { get; set; }
    [JsonPropertyName("strVideo")] public string? StrVideo { get; set; }
    [JsonPropertyName("strThumb")] public string? StrThumb { get; set; }
}

internal sealed class TableResponse
{
    [JsonPropertyName("table")]
    public List<TableRowDto>? Table { get; set; }
}

internal sealed class TableRowDto
{
    [JsonPropertyName("intRank")] public string? IntRank { get; set; }
    [JsonPropertyName("idTeam")] public string? IdTeam { get; set; }
    [JsonPropertyName("strTeam")] public string? StrTeam { get; set; }
    [JsonPropertyName("strBadge")] public string? StrBadge { get; set; }
    [JsonPropertyName("strGroup")] public string? StrGroup { get; set; }
    [JsonPropertyName("intPlayed")] public string? IntPlayed { get; set; }
    [JsonPropertyName("intWin")] public string? IntWin { get; set; }
    [JsonPropertyName("intDraw")] public string? IntDraw { get; set; }
    [JsonPropertyName("intLoss")] public string? IntLoss { get; set; }
    [JsonPropertyName("intPoints")] public string? IntPoints { get; set; }
    [JsonPropertyName("strForm")] public string? StrForm { get; set; }
    [JsonPropertyName("strSeason")] public string? StrSeason { get; set; }
}

internal sealed class LeaguesResponse
{
    [JsonPropertyName("leagues")]
    public List<LeagueDto>? Leagues { get; set; }
}

internal sealed class LeagueDto
{
    [JsonPropertyName("idLeague")] public string? IdLeague { get; set; }
    [JsonPropertyName("strLeague")] public string? StrLeague { get; set; }
    [JsonPropertyName("strSport")] public string? StrSport { get; set; }
    [JsonPropertyName("strCurrentSeason")] public string? StrCurrentSeason { get; set; }
    [JsonPropertyName("strBadge")] public string? StrBadge { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TeamsResponse))]
[JsonSerializable(typeof(EventsResponse))]
[JsonSerializable(typeof(ResultsResponse))]
[JsonSerializable(typeof(TableResponse))]
[JsonSerializable(typeof(LeaguesResponse))]
internal sealed partial class SportsDbJsonContext : JsonSerializerContext;
