using System.Buffers.Binary;
using System.Globalization;
using TraySport.Core.Api;
using TraySport.Core.Formatting;
using TraySport.Core.Icons;
using TraySport.Core.Models;

namespace TraySport.Tests;

[TestClass]
public sealed class GameFormatterTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static Game Scored(int home, int away) =>
        new("1", "Lakers", "Warriors", "10", "20", home, away, new DateTimeOffset(2026, 7, 19, 0, 30, 0, TimeSpan.Zero), true);

    [TestMethod]
    public void Outcome_And_Score_AreFromTheTeamsSide()
    {
        Game game = Scored(88, 92);

        Assert.AreEqual(GameOutcome.Loss, GameFormatter.Outcome(game, "10"));
        Assert.AreEqual(GameOutcome.Win, GameFormatter.Outcome(game, "20"));
        Assert.AreEqual("88–92", GameFormatter.Score(game, "10"));
        Assert.AreEqual("92–88", GameFormatter.Score(game, "20"));
        Assert.AreEqual("vs Warriors", GameFormatter.Matchup(game, "10"));
        Assert.AreEqual("@ Lakers", GameFormatter.Matchup(game, "20"));
        Assert.AreEqual(GameOutcome.Draw, GameFormatter.Outcome(Scored(1, 1), "10"));
        Assert.AreEqual(GameOutcome.Unknown, GameFormatter.Outcome(game, "99"));
    }

    [TestMethod]
    public void RecapUrl_PrefersTheEventVideo_ElseSearchesYouTube()
    {
        Game game = Scored(1, 0);
        Assert.AreEqual(
            "https://www.youtube.com/results?search_query=Lakers%20vs%20Warriors%20highlights%20July%2019%202026",
            GameFormatter.RecapUrl(game));

        Assert.AreEqual("https://youtu.be/x", GameFormatter.RecapUrl(game with { VideoUrl = "https://youtu.be/x" }));
    }

    [TestMethod]
    public void When_UsesRelativeDays()
    {
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        DateTimeOffset now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

        Assert.AreEqual("Today 7:30 PM", GameFormatter.When(now.AddHours(7.5), true, now, utc, EnUs));
        Assert.AreEqual("Tomorrow 3:00 PM", GameFormatter.When(now.AddHours(27), true, now, utc, EnUs));
        Assert.AreEqual("Yesterday", GameFormatter.When(now.AddDays(-1), true, now, utc, EnUs));
        Assert.AreEqual("Thu 12:00 PM", GameFormatter.When(now.AddDays(4), true, now, utc, EnUs));
        Assert.AreEqual("Sep 20", GameFormatter.When(now.AddDays(-7), true, now, utc, EnUs));
        Assert.AreEqual("Tomorrow", GameFormatter.When(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), false, now, utc, EnUs));
        Assert.AreEqual("Date to be announced", GameFormatter.When(null, false, now, utc, EnUs));
    }

    [TestMethod]
    public void TooltipLine_SummarizesTheLastResult()
    {
        Assert.AreEqual("Lakers L 88–92 vs Warriors", GameFormatter.TooltipLine("Lakers", Scored(88, 92), "10"));
        Assert.IsNull(GameFormatter.TooltipLine("Lakers", null, "10"));
    }
}

[TestClass]
public sealed class FavoritesPolicyTests
{
    private static FavoriteTeam Fav(string id) => new(id, "Team " + id, "Soccer", null, null, null);

    [TestMethod]
    public void Add_DedupesAndCaps()
    {
        IReadOnlyList<FavoriteTeam> list = [];
        for (int i = 0; i < FavoritesPolicy.MaxFavorites + 3; i++)
        {
            list = FavoritesPolicy.Add(list, Fav(i.ToString(CultureInfo.InvariantCulture)));
        }

        Assert.AreEqual(FavoritesPolicy.MaxFavorites, list.Count);
        list = FavoritesPolicy.Add(list, Fav("0") with { Name = "Renamed" });
        Assert.AreEqual("Renamed", list[0].Name);
        Assert.IsFalse(FavoritesPolicy.CanAdd(list, "new"));
    }

    [TestMethod]
    public void Move_And_Remove()
    {
        IReadOnlyList<FavoriteTeam> list = [Fav("a"), Fav("b"), Fav("c")];

        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, FavoritesPolicy.Move(list, "b", -1).Select(f => f.Id).ToArray());
        Assert.AreSame(list, FavoritesPolicy.Move(list, "a", -1));
        CollectionAssert.AreEqual(new[] { "a", "c" }, FavoritesPolicy.Remove(list, "b").Select(f => f.Id).ToArray());
    }
}

[TestClass]
public sealed class TrayIconChoiceTests
{
    [TestMethod]
    [DataRow("team:133604", true, "133604")]
    [DataRow("sport:basketball", false, "basketball")]
    [DataRow("sport:Basketball", false, "basketball")]
    [DataRow("sport:curling", false, "soccer")]
    [DataRow("team:abc", false, "soccer")]
    [DataRow(null, false, "soccer")]
    [DataRow("garbage", false, "soccer")]
    public void Parse(string? input, bool isTeam, string key)
    {
        TrayIconChoice choice = TrayIconChoice.Parse(input);
        Assert.AreEqual(isTeam, choice.IsTeam);
        Assert.AreEqual(key, choice.Key);
        Assert.AreEqual(choice, TrayIconChoice.Parse(choice.ToString()));
    }

    [TestMethod]
    public void EverySportHasAnIconKey() =>
        Assert.IsTrue(SportCatalog.Sports.All(s => SportCatalog.FindByIconKey(s.IconKey) == s));
}

[TestClass]
public sealed class IcoWriterTests
{
    [TestMethod]
    public void Write_ProducesDirectoryPointingAtEachPng()
    {
        byte[] a = [1, 2, 3];
        byte[] b = [4, 5, 6, 7];
        byte[] ico = IcoWriter.Write([(16, a), (256, b)]);

        Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.AreEqual(2, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));
        Assert.AreEqual(16, ico[6]);
        Assert.AreEqual(0, ico[6 + 16], "256 px is written as 0.");

        int offsetA = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 12));
        int offsetB = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 16 + 12));
        CollectionAssert.AreEqual(a, ico[offsetA..(offsetA + 3)]);
        CollectionAssert.AreEqual(b, ico[offsetB..(offsetB + 4)]);
        Assert.AreEqual(ico.Length, offsetB + b.Length);
    }
}

[TestClass]
public sealed class RequestGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DelayFor_UnderLimit_IsZero() =>
        Assert.AreEqual(TimeSpan.Zero, RequestGate.DelayFor([Now.AddSeconds(-10)], 2, TimeSpan.FromMinutes(1), Now, DateTimeOffset.MinValue));

    [TestMethod]
    public void DelayFor_AtLimit_WaitsForOldestToExpire() =>
        Assert.AreEqual(TimeSpan.FromSeconds(20),
            RequestGate.DelayFor([Now.AddSeconds(-40), Now.AddSeconds(-5)], 2, TimeSpan.FromMinutes(1), Now, DateTimeOffset.MinValue));

    [TestMethod]
    public void DelayFor_Blocked_WaitsForBlock() =>
        Assert.AreEqual(TimeSpan.FromSeconds(45),
            RequestGate.DelayFor([], 2, TimeSpan.FromMinutes(1), Now, Now.AddSeconds(45)));
}
