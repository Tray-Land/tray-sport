using System.Text.Json.Serialization;

namespace TraySport.Core.Models;

/// <summary>
/// A starred team: enough to draw its card before any network call. The last three fields are the
/// off-season snooze (see <see cref="SnoozePolicy"/>); older saved lists lack them and read as awake.
/// </summary>
public sealed record FavoriteTeam(
    string Id,
    string Name,
    string Sport,
    string? League,
    string? LeagueId,
    string? BadgeUrl,
    DateTimeOffset? SnoozedAt = null,
    DateTimeOffset? ProbedAt = null,
    DateTimeOffset? WokenAt = null)
{
    public static FavoriteTeam From(Team team) => new(team.Id, team.Name, team.Sport, team.League, team.LeagueId, team.BadgeUrl);

    public string? TinyBadgeUrl => ImageUrls.Tiny(BadgeUrl);

    [JsonIgnore]
    public bool IsSnoozed => SnoozedAt is not null;
}

/// <summary>
/// When a team goes quiet for the off-season and when it comes back. A team with no next game a
/// week after its last one is snoozed; while snoozed it costs no refresh requests, only one cheap
/// "next game" check a week. The first check that finds a game wakes it.
/// </summary>
public static class SnoozePolicy
{
    /// <summary>How long after the last game (or a manual wake) with nothing scheduled before auto-snoozing.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromDays(7);

    /// <summary>How often a snoozed team looks for its next game.</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromDays(7);

    /// <summary>
    /// True when a loaded team has a last game at least <see cref="QuietPeriod"/> old and no next
    /// game. A manual wake restarts the clock so the team isn't snoozed again straight away.
    /// A team with no last game is never auto-snoozed: that is as likely a data gap as an off-season.
    /// </summary>
    public static bool ShouldAutoSnooze(FavoriteTeam team, Game? last, Game? next, DateTimeOffset now)
    {
        if (team.IsSnoozed || next is not null || last?.StartsAt is not { } lastStart)
        {
            return false;
        }

        DateTimeOffset quietSince = team.WokenAt is { } woken && woken > lastStart ? woken : lastStart;
        return now - quietSince >= QuietPeriod;
    }

    public static bool IsProbeDue(FavoriteTeam team, DateTimeOffset now) =>
        team.SnoozedAt is { } snoozed && now - (team.ProbedAt ?? snoozed) >= ProbeInterval;
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
            // Fresher details keep the team's snooze state.
            updated[index] = team with
            {
                SnoozedAt = current[index].SnoozedAt,
                ProbedAt = current[index].ProbedAt,
                WokenAt = current[index].WokenAt,
            };
            return updated;
        }

        return current.Count >= MaxFavorites ? current : [.. current, team];
    }

    public static IReadOnlyList<FavoriteTeam> Remove(IReadOnlyList<FavoriteTeam> current, string teamId) =>
        current.Where(f => f.Id != teamId).ToList();

    /// <summary>Snoozes the team (a no-op when it is missing or already snoozed).</summary>
    public static IReadOnlyList<FavoriteTeam> Snooze(IReadOnlyList<FavoriteTeam> current, string teamId, DateTimeOffset now) =>
        Update(current, teamId, t => t.IsSnoozed ? t : t with { SnoozedAt = now, ProbedAt = now });

    /// <summary>Wakes the team and remembers when, so auto-snooze waits a full quiet period again.</summary>
    public static IReadOnlyList<FavoriteTeam> Wake(IReadOnlyList<FavoriteTeam> current, string teamId, DateTimeOffset now) =>
        Update(current, teamId, t => t.IsSnoozed ? t with { SnoozedAt = null, ProbedAt = null, WokenAt = now } : t);

    /// <summary>Records that a snoozed team was checked and had no game yet.</summary>
    public static IReadOnlyList<FavoriteTeam> MarkProbed(IReadOnlyList<FavoriteTeam> current, string teamId, DateTimeOffset now) =>
        Update(current, teamId, t => t.IsSnoozed ? t with { ProbedAt = now } : t);

    private static IReadOnlyList<FavoriteTeam> Update(IReadOnlyList<FavoriteTeam> current, string teamId, Func<FavoriteTeam, FavoriteTeam> change)
    {
        int index = current.ToList().FindIndex(f => f.Id == teamId);
        if (index < 0)
        {
            return current;
        }

        FavoriteTeam changed = change(current[index]);
        if (changed == current[index])
        {
            return current;
        }

        List<FavoriteTeam> updated = [.. current];
        updated[index] = changed;
        return updated;
    }

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
