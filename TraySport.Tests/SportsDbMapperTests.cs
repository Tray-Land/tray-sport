using System.Net;
using TraySport.Core.Api;
using TraySport.Core.Models;

namespace TraySport.Tests;

[TestClass]
public sealed class SportsDbMapperTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    [TestMethod]
    public void ParseTeams_ReadsTeamAndNormalizesLinks()
    {
        Team team = SportsDbMapper.ParseTeams(Sample("team-arsenal.json")).Single();

        Assert.AreEqual("133604", team.Id);
        Assert.AreEqual("Arsenal", team.Name);
        Assert.AreEqual("Soccer", team.Sport);
        Assert.AreEqual("4328", team.LeagueId);
        Assert.AreEqual(1892, team.FormedYear);
        Assert.AreEqual("https://www.arsenal.com/", team.Website);
        Assert.AreEqual("https://www.youtube.com/user/ArsenalTour", team.YouTube);
        Assert.IsNull(team.BannerUrl, "Empty strings become null.");
        Assert.AreEqual("https://r2.thesportsdb.com/images/media/team/badge/uyhbfe1612467038.png/tiny", team.TinyBadgeUrl);
    }

    [TestMethod]
    public void ParseGames_ReadsResultsArrayFromEventsLast()
    {
        Game game = SportsDbMapper.ParseGames(Sample("eventslast-lakers.json")).Single();

        Assert.AreEqual("2541548", game.Id);
        Assert.AreEqual(88, game.HomeScore);
        Assert.AreEqual(92, game.AwayScore);
        Assert.AreEqual(new DateTimeOffset(2026, 7, 19, 0, 30, 0, TimeSpan.Zero), game.StartsAt);
        Assert.IsTrue(game.HasStartTime);
        Assert.IsNull(game.VideoUrl);
        Assert.AreEqual("Golden State Warriors", game.OpponentOf("134867"));
    }

    [TestMethod]
    public void ParseGames_ReadsEventsArrayWithoutScores()
    {
        Game game = SportsDbMapper.ParseGames(Sample("eventsnext-arsenal.json")).Single();

        Assert.IsFalse(game.HasScore);
        Assert.IsFalse(game.IsHome("133604"));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero), game.StartsAt);
    }

    [TestMethod]
    public void ParseTable_SortsByRank()
    {
        IReadOnlyList<StandingRow> rows = SportsDbMapper.ParseTable(Sample("table-epl.json"));

        CollectionAssert.AreEqual(new[] { "Manchester City", "Arsenal" }, rows.Select(r => r.Team).ToArray());
        Assert.AreEqual(13, rows[1].Points);
        Assert.AreEqual("WWDLW", rows[1].Form);
    }

    [TestMethod]
    [DataRow("{\"teams\":null}")]
    [DataRow("{\"events\":null}")]
    [DataRow("not json")]
    [DataRow("")]
    public void Parse_EmptyOrInvalid_ReturnsEmpty(string json)
    {
        Assert.AreEqual(0, SportsDbMapper.ParseTeams(json).Count);
        Assert.AreEqual(0, SportsDbMapper.ParseGames(json).Count);
        Assert.AreEqual(0, SportsDbMapper.ParseTable(json).Count);
    }

    [TestMethod]
    public void ParseStart_DateOnly_HasNoTime()
    {
        (DateTimeOffset? start, bool hasTime) = SportsDbMapper.ParseStart(null, "2026-10-03", null);

        Assert.AreEqual(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), start);
        Assert.IsFalse(hasTime);
    }

    [TestMethod]
    public void ParseStart_DateAndTime_IsUtc()
    {
        (DateTimeOffset? start, bool hasTime) = SportsDbMapper.ParseStart("", "2026-10-03", "19:45:00+00:00");

        Assert.AreEqual(new DateTimeOffset(2026, 10, 3, 19, 45, 0, TimeSpan.Zero), start);
        Assert.IsTrue(hasTime);
    }

    [TestMethod]
    [DataRow("www.arsenal.com", "https://www.arsenal.com/")]
    [DataRow("http://example.org/a", "http://example.org/a")]
    [DataRow("  ", null)]
    [DataRow("nothing", null)]
    public void NormalizeUrl(string input, string? expected) => Assert.AreEqual(expected, SportsDbMapper.NormalizeUrl(input));

    [TestMethod]
    public void IsRateLimited_DetectsCloudflare1015AndHttp429()
    {
        Assert.IsTrue(SportsDbClient.IsRateLimited(HttpStatusCode.OK, "error code: 1015"));
        Assert.IsTrue(SportsDbClient.IsRateLimited(HttpStatusCode.TooManyRequests, ""));
        Assert.IsFalse(SportsDbClient.IsRateLimited(HttpStatusCode.OK, "{\"teams\":null}"));
    }
}
