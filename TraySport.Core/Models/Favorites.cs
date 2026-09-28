using System.Text.Json.Serialization;

namespace TraySport.Core.Models;

/// <summary>A starred team: enough to draw its card before any network call.</summary>
public sealed record FavoriteTeam(string Id, string Name, string Sport, string? League, string? LeagueId, string? BadgeUrl)
{
    public static FavoriteTeam From(Team team) => new(team.Id, team.Name, team.Sport, team.League, team.LeagueId, team.BadgeUrl);

    public string? TinyBadgeUrl => ImageUrls.Tiny(BadgeUrl);
}

/// <summary>List rules for favorites, kept pure for tests.</summary>
public static class FavoritesPolicy
{
    /// <summary>
    /// Each favorite costs two requests per refresh, and the free API allows 30 a minute; ten
    /// favorites refresh in one go with room to spare for browsing.
    /// </summary>
    public const int MaxFavorites = 10;

    public static bool CanAdd(IReadOnlyList<FavoriteTeam> current, string teamId) =>
        current.Count < MaxFavorites && current.All(f => f.Id != teamId);

    /// <summary>Adds (at the end) or updates in place; returns the list unchanged when full.</summary>
    public static IReadOnlyList<FavoriteTeam> Add(IReadOnlyList<FavoriteTeam> current, FavoriteTeam team)
    {
        int index = current.ToList().FindIndex(f => f.Id == team.Id);
        if (index >= 0)
        {
            List<FavoriteTeam> updated = [.. current];
            updated[index] = team;
            return updated;
        }

        return current.Count >= MaxFavorites ? current : [.. current, team];
    }

    public static IReadOnlyList<FavoriteTeam> Remove(IReadOnlyList<FavoriteTeam> current, string teamId) =>
        current.Where(f => f.Id != teamId).ToList();

    /// <summary>Moves a favorite up (-1) or down (+1); out-of-range moves are ignored.</summary>
    public static IReadOnlyList<FavoriteTeam> Move(IReadOnlyList<FavoriteTeam> current, string teamId, int delta)
    {
        List<FavoriteTeam> list = [.. current];
        int from = list.FindIndex(f => f.Id == teamId);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count)
        {
            return current;
        }

        (list[from], list[to]) = (list[to], list[from]);
        return list;
    }
}

[JsonSerializable(typeof(List<FavoriteTeam>))]
public sealed partial class FavoritesJsonContext : JsonSerializerContext;
