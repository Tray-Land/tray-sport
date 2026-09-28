using System.Globalization;
using TraySport.Core.Models;

namespace TraySport.Core.Formatting;

/// <summary>Text for games and links out, from a favorite team's point of view.</summary>
public static class GameFormatter
{
    public static GameOutcome Outcome(Game game, string teamId)
    {
        if (!game.HasScore || (game.HomeTeamId != teamId && game.AwayTeamId != teamId))
        {
            return GameOutcome.Unknown;
        }

        int mine = game.IsHome(teamId) ? game.HomeScore!.Value : game.AwayScore!.Value;
        int theirs = game.IsHome(teamId) ? game.AwayScore!.Value : game.HomeScore!.Value;
        return mine > theirs ? GameOutcome.Win : mine < theirs ? GameOutcome.Loss : GameOutcome.Draw;
    }

    /// <summary>"W", "L", "D", or "" when there's no score.</summary>
    public static string OutcomeLetter(GameOutcome outcome) => outcome switch
    {
        GameOutcome.Win => "W",
        GameOutcome.Loss => "L",
        GameOutcome.Draw => "D",
        _ => "",
    };

    /// <summary>The score with the given team's points first: "3–1". Empty without a score.</summary>
    public static string Score(Game game, string teamId)
    {
        if (!game.HasScore)
        {
            return "";
        }

        return game.IsHome(teamId)
            ? $"{game.HomeScore}–{game.AwayScore}"
            : $"{game.AwayScore}–{game.HomeScore}";
    }

    /// <summary>"vs Chelsea" at home, "@ Chelsea" away.</summary>
    public static string Matchup(Game game, string teamId) =>
        (game.IsHome(teamId) ? "vs " : "@ ") + game.OpponentOf(teamId);

    /// <summary>Both teams, home last in the US style is ambiguous across sports, so: "Home vs Away".</summary>
    public static string Title(Game game) => $"{game.HomeTeam} vs {game.AwayTeam}";

    /// <summary>
    /// A short, relative date: "Today 7:30 PM", "Tomorrow", "Sat 3:00 PM", "Yesterday", "Sep 20".
    /// Without a known time only the day is shown.
    /// </summary>
    public static string When(DateTimeOffset? startsAt, bool hasTime, DateTimeOffset now, TimeZoneInfo zone, CultureInfo culture)
    {
        if (startsAt is null)
        {
            return "Date to be announced";
        }

        DateTime local = TimeZoneInfo.ConvertTime(startsAt.Value, zone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        int days = (int)(local.Date - today).TotalDays;

        // A date-only event is stored at midnight UTC; show that UTC date instead of shifting it.
        if (!hasTime)
        {
            local = startsAt.Value.UtcDateTime;
            days = (int)(local.Date - today).TotalDays;
        }

        string day = days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and < 7 => local.ToString("ddd", culture),
            _ => local.ToString(culture.DateTimeFormat.MonthDayPattern.Contains("MMMM", StringComparison.Ordinal)
                ? culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal)
                : culture.DateTimeFormat.MonthDayPattern, culture),
        };

        if (local.Year != today.Year && Math.Abs(days) > 180)
        {
            day += local.ToString(" yyyy", culture);
        }

        // Past games only need the day; upcoming ones need the time.
        return hasTime && days >= 0 ? $"{day} {local.ToString("t", culture)}" : day;
    }

    /// <summary>
    /// The game's own video when TheSportsDB has one, else a YouTube search that reliably finds the
    /// league's official recap.
    /// </summary>
    public static string RecapUrl(Game game)
    {
        if (game.VideoUrl is { } video)
        {
            return video;
        }

        string date = game.StartsAt is { } s ? s.UtcDateTime.ToString("MMMM d yyyy", CultureInfo.InvariantCulture) : "";
        return YouTubeSearchUrl($"{game.HomeTeam} vs {game.AwayTeam} highlights {date}");
    }

    public static string TeamHighlightsUrl(Team team) => YouTubeSearchUrl($"{team.Name} highlights");

    public static string YouTubeSearchUrl(string query) =>
        "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(string.Join(' ', query.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    /// <summary>"Arsenal W 3–1 vs Chelsea" for the tray tooltip; null without a scored game.</summary>
    public static string? TooltipLine(string teamName, Game? last, string teamId)
    {
        if (last is null || !last.HasScore)
        {
            return null;
        }

        string letter = OutcomeLetter(Outcome(last, teamId));
        return $"{teamName} {letter} {Score(last, teamId)} {Matchup(last, teamId)}".Replace("  ", " ", StringComparison.Ordinal);
    }
}
