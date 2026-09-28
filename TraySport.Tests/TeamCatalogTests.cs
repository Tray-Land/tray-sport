using TraySport.Core.Models;

namespace TraySport.Tests;

[TestClass]
public sealed class TeamCatalogTests
{
    private const string Json = """
        {"generated":"2026-09-27","teams":[
          {"id":"134867","name":"Los Angeles Lakers","short":"LAL","alt":null,"sport":"Basketball","league":"NBA","leagueId":"4387","badge":"https://x/l.png","country":"United States"},
          {"id":"134866","name":"Los Angeles Clippers","short":"LAC","alt":"LA Clippers","sport":"Basketball","league":"NBA","leagueId":"4387","badge":null,"country":"United States"},
          {"id":"133729","name":"Atlético Madrid","short":"ATM","alt":"Atleti, Club Atlético de Madrid","sport":"Soccer","league":"Spanish La Liga","leagueId":"4335","badge":null,"country":"Spain"},
          {"id":"133738","name":"Real Madrid","short":"RMA","alt":null,"sport":"Soccer","league":"Spanish La Liga","leagueId":"4335","badge":null,"country":"Spain"},
          {"id":"","name":"Broken","leagueId":"4335"}
        ]}
        """;

    private static readonly TeamCatalog Catalog = TeamCatalog.Parse(Json);

    [TestMethod]
    public void Parse_SkipsEntriesWithoutId() => Assert.AreEqual(4, Catalog.Count);

    [TestMethod]
    public void TeamsIn_ListsTheWholeLeagueByName()
    {
        CollectionAssert.AreEqual(new[] { "Los Angeles Clippers", "Los Angeles Lakers" }, Catalog.TeamsIn("4387").Select(t => t.Name).ToArray());
        Assert.IsFalse(Catalog.HasLeague("4391"));
    }

    [TestMethod]
    public void Search_MatchesAWordInTheName() =>
        Assert.AreEqual("Los Angeles Lakers", Catalog.Search("lakers").First().Name);

    [TestMethod]
    public void Search_IgnoresAccentsAndMatchesAlternates()
    {
        Assert.AreEqual("Atlético Madrid", Catalog.Search("atletico").First().Name);
        Assert.AreEqual("Atlético Madrid", Catalog.Search("Atleti").First().Name);
        Assert.AreEqual("Los Angeles Clippers", Catalog.Search("LA Clippers").First().Name);
    }

    [TestMethod]
    public void Search_RanksPrefixAboveWordAboveContains()
    {
        CollectionAssert.AreEquivalent(new[] { "Atlético Madrid", "Real Madrid" }, Catalog.Search("madrid").Select(t => t.Name).ToArray());
        Assert.AreEqual("Real Madrid", Catalog.Search("real").First().Name, "Prefix match.");
        Assert.AreEqual("Los Angeles Clippers", Catalog.Search("los angeles c").First().Name);
        Assert.AreEqual(2, Catalog.Search("ngeles").Count, "Plain substring still matches.");
    }

    [TestMethod]
    public void Search_TooShortOrInvalid_ReturnsNothing()
    {
        Assert.AreEqual(0, Catalog.Search("l").Count);
        Assert.AreEqual(0, TeamCatalog.Parse("nope").Count);
    }
}
