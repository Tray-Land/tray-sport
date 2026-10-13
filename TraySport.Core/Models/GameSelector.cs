namespace TraySport.Core.Models;

/// <summary>
/// Picks a team's last and next game from the candidates TheSportsDB returns. The free tier's
/// eventsnext/eventslast give one game each, and that game is sometimes stale (already final, or
/// days old), so the choice needs a rule instead of trusting the first row.
/// </summary>
public static class GameSelector
{
    /// <summary>A game still marked "not started" this long after its start is treated as stale data.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    private static readonly HashSet<string> FinalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "AET", "PEN", "AOT", "AP", "AW", "FT_PEN", "Match Finished", "Finished", "Final",
    };

    public static bool IsFinal(Game game) => game.Status is { } s && FinalStatuses.Contains(s.Trim());

    /// <summary>True when the game can't be "next": it is over, or it should have started long ago without ever updating.</summary>
    public static bool IsStale(Game game, DateTimeOffset now) =>
        IsFinal(game) || (game.StartsAt is { } start && now - start > StaleAfter);

    public static bool InvolvesTeam(Game game, string teamId) => game.HomeTeamId == teamId || game.AwayTeamId == teamId;

    /// <summary>The earliest game of the team that is neither over nor stale.</summary>
    public static Game? PickNext(IEnumerable<Game> games, string teamId, DateTimeOffset now) =>
        games.Where(g => InvolvesTeam(g, teamId) && !IsStale(g, now))
            .OrderBy(g => g.StartsAt ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

    /// <summary>
    /// The most recent finished game. <paramref name="past"/> (eventslast) is trusted as is;
    /// <paramref name="upcoming"/> (eventsnext) only counts when it is already final, which is how
    /// a finished game that the API still lists as "next" gets shown as the last result.
    /// </summary>
    public static Game? PickLast(IEnumerable<Game> past, IEnumerable<Game> upcoming, string teamId) =>
        past.Concat(upcoming.Where(IsFinal))
            .Where(g => InvolvesTeam(g, teamId))
            .OrderByDescending(g => g.StartsAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
}
