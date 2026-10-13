using TraySport.Core.Models;

namespace TraySport.Tests;

[TestClass]
public sealed class GameSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);

    private static Game Make(string id, DateTimeOffset? start, string? status, string home = "1", string away = "2") =>
        new(id, "Home", "Away", home, away, null, null, start, start is not null) { Status = status };

    [TestMethod]
    [DataRow("FT", true)]
    [DataRow("ft", true)]
    [DataRow("Match Finished", true)]
    [DataRow("NS", false)]
    [DataRow(null, false)]
    public void IsFinal_RecognisesFinishedStatuses(string? status, bool expected) =>
        Assert.AreEqual(expected, GameSelector.IsFinal(Make("g", Now, status)));

    [TestMethod]
    public void IsStale_FinalGame_IsStale() =>
        Assert.IsTrue(GameSelector.IsStale(Make("g", Now.AddHours(1), "FT"), Now));

    [TestMethod]
    public void IsStale_NotStartedLongAgo_IsStale() =>
        Assert.IsTrue(GameSelector.IsStale(Make("g", Now.AddHours(-13), "NS"), Now));

    [TestMethod]
    public void IsStale_RecentlyStartedGame_IsNotStale() =>
        Assert.IsFalse(GameSelector.IsStale(Make("g", Now.AddHours(-2), "NS"), Now));

    [TestMethod]
    public void PickNext_SkipsFinishedAndOtherTeams_AndTakesEarliest()
    {
        Game[] games =
        [
            Make("over", Now.AddHours(-14), "FT"),
            Make("other", Now.AddHours(1), "NS", home: "8", away: "9"),
            Make("later", Now.AddDays(2), "NS"),
            Make("soon", Now.AddHours(5), "NS"),
        ];

        Assert.AreEqual("soon", GameSelector.PickNext(games, "1", Now)?.Id);
    }

    [TestMethod]
    public void PickNext_OnlyStaleGames_ReturnsNull() =>
        Assert.IsNull(GameSelector.PickNext([Make("over", Now.AddHours(-3), "FT")], "1", Now));

    [TestMethod]
    public void PickLast_UsesFinishedUpcomingRow_WhenNewerThanPast()
    {
        Game[] past = [Make("old", Now.AddDays(-8), "FT")];
        Game[] upcoming = [Make("yesterday", Now.AddHours(-14), "FT")];

        Assert.AreEqual("yesterday", GameSelector.PickLast(past, upcoming, "1")?.Id);
    }

    [TestMethod]
    public void PickLast_IgnoresUpcomingRowsThatAreNotFinal()
    {
        Game[] past = [Make("old", Now.AddDays(-8), "FT")];
        Game[] upcoming = [Make("live", Now.AddHours(-1), "1H")];

        Assert.AreEqual("old", GameSelector.PickLast(past, upcoming, "1")?.Id);
    }
}
