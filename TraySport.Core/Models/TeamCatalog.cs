using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraySport.Core.Models;

/// <summary>
/// The teams of every browsable league, shipped with the app (Assets\teams.json, built by
/// scripts\BuildTeamCatalog.cs). The free API lists only 10 teams per league and returns a single,
/// often wrong, result per search; the catalog makes browsing complete and search forgiving.
/// </summary>
public sealed class TeamCatalog
{
    private readonly List<Entry> _entries;

    private TeamCatalog(List<Entry> entries) => _entries = entries;

    public static TeamCatalog Empty { get; } = new([]);

    public int Count => _entries.Count;

    public static TeamCatalog Parse(string json)
    {
        try
        {
            CatalogFile? file = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.CatalogFile);
            List<Entry> entries = file?.Teams?
                .Where(t => !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => new Entry(
                    new Team(t.Id!, t.Name!, t.Sport ?? "", t.League, t.LeagueId, t.Badge) { ShortName = t.Short, Country = t.Country },
                    Keys(t)))
                .ToList() ?? [];
            return new TeamCatalog(entries);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public bool HasLeague(string leagueId) => _entries.Any(e => e.Team.LeagueId == leagueId);

    public IReadOnlyList<Team> TeamsIn(string leagueId) =>
        _entries.Where(e => e.Team.LeagueId == leagueId)
            .Select(e => e.Team)
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Teams whose name, short code, or alternate names match the query, best first: an exact
    /// name, then a name or word that starts with it ("lakers" finds "Los Angeles Lakers"), then
    /// anything containing it.
    /// </summary>
    public IReadOnlyList<Team> Search(string query, int max = 10)
    {
        string q = Normalize(query);
        if (q.Length < 2)
        {
            return [];
        }

        return _entries
            .Select(e => (e.Team, Score: Score(e, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Team.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(x => x.Team)
            .ToList();
    }

    private static int Score(Entry entry, string q)
    {
        int best = 0;
        foreach (string key in entry.Keys)
        {
            int score = key == q ? 100
                : key.StartsWith(q, StringComparison.Ordinal) ? 80
                : key.Contains(" " + q, StringComparison.Ordinal) ? 70
                : key.Contains(q, StringComparison.Ordinal) ? 40
                : 0;

            // The primary name beats an alternate spelling at the same level.
            if (score > 0 && key == entry.Keys[0])
            {
                score += 5;
            }

            best = Math.Max(best, score);
        }

        return best;
    }

    private static string[] Keys(CatalogTeam t)
    {
        List<string> keys = [Normalize(t.Name!)];
        if (!string.IsNullOrWhiteSpace(t.Short))
        {
            keys.Add(Normalize(t.Short));
        }

        if (!string.IsNullOrWhiteSpace(t.Alt))
        {
            keys.AddRange(t.Alt.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Normalize));
        }

        return [.. keys.Where(k => k.Length > 0).Distinct()];
    }

    /// <summary>Lowercase, accents removed, punctuation as spaces: "Atlético" and "atletico" match.</summary>
    public static string Normalize(string value)
    {
        StringBuilder sb = new(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record Entry(Team Team, string[] Keys);
}

internal sealed class CatalogFile
{
    [JsonPropertyName("generated")] public string? Generated { get; set; }
    [JsonPropertyName("teams")] public List<CatalogTeam>? Teams { get; set; }
}

internal sealed class CatalogTeam
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("short")] public string? Short { get; set; }
    [JsonPropertyName("alt")] public string? Alt { get; set; }
    [JsonPropertyName("sport")] public string? Sport { get; set; }
    [JsonPropertyName("league")] public string? League { get; set; }
    [JsonPropertyName("leagueId")] public string? LeagueId { get; set; }
    [JsonPropertyName("badge")] public string? Badge { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

[JsonSerializable(typeof(CatalogFile))]
internal sealed partial class CatalogJsonContext : JsonSerializerContext;
