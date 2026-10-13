using System.Net;
using TraySport.Core.Api;

namespace TraySport.Tests;

[TestClass]
public sealed class SportsDbClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [TestMethod]
    public async Task EmptyBody_IsNoData()
    {
        SportsDbClient client = new(new HttpClient(new FakeHandler(_ => Ok(""))));
        Assert.AreEqual(0, (await client.StandingsAsync("4387", "2026-2027")).Count);
    }

    [TestMethod]
    public async Task Responses_AreCachedAndShared()
    {
        FakeHandler handler = new(_ => Ok("""{"results":null}"""));
        SportsDbClient client = new(new HttpClient(handler));

        await Task.WhenAll(client.LastGameAsync("1"), client.LastGameAsync("1"));
        await client.LastGameAsync("1");

        // eventslast + eventsnext, once each.
        Assert.AreEqual(2, handler.Calls);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private const string StaleFinalNext = """
        {"events":[{"idEvent":"2616723","strTimestamp":"2026-10-12T00:00:00","strHomeTeam":"Milwaukee Brewers","strAwayTeam":"Los Angeles Dodgers",
        "idHomeTeam":"135274","idAwayTeam":"135263","idLeague":"4424","intHomeScore":"2","intAwayScore":"1","strStatus":"FT"}]}
        """;

    private const string OlderLast = """
        {"results":[{"idEvent":"2601000","strTimestamp":"2026-10-04T20:00:00","strHomeTeam":"Milwaukee Brewers","strAwayTeam":"San Diego Padres",
        "idHomeTeam":"135274","idAwayTeam":"135270","idLeague":"4424","intHomeScore":"4","intAwayScore":"3","strStatus":"FT"}]}
        """;

    private const string LeagueDay = """
        {"events":[
        {"idEvent":"2616723","strTimestamp":"2026-10-12T00:00:00","strHomeTeam":"Milwaukee Brewers","strAwayTeam":"Los Angeles Dodgers","idHomeTeam":"135274","idAwayTeam":"135263","idLeague":"4424","strStatus":"FT"},
        {"idEvent":"2616724","strTimestamp":"2026-10-12T21:00:00","strHomeTeam":"Milwaukee Brewers","strAwayTeam":"Los Angeles Dodgers","idHomeTeam":"135274","idAwayTeam":"135263","idLeague":"4424","strStatus":"NS"}]}
        """;

    private static SportsDbClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHandler handler)
    {
        handler = new FakeHandler(respond);
        return new SportsDbClient(new HttpClient(handler), time: new FixedTime(new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero)));
    }

    private static HttpResponseMessage Route(HttpRequestMessage r) => r.RequestUri!.AbsolutePath switch
    {
        var p when p.EndsWith("eventsnext.php") => Ok(StaleFinalNext),
        var p when p.EndsWith("eventslast.php") => Ok(OlderLast),
        var p when p.EndsWith("eventsday.php") => r.RequestUri.Query.Contains("d=2026-10-12") ? Ok(LeagueDay) : Ok("""{"events":null}"""),
        _ => Ok("{}"),
    };

    [TestMethod]
    public async Task NextGame_WhenEventsNextIsFinal_FallsBackToLeagueDay()
    {
        SportsDbClient client = ClientFor(Route, out _);

        var next = await client.NextGameAsync("135274");

        Assert.AreEqual("2616724", next?.Id);
    }

    [TestMethod]
    public async Task NextGame_WhenEventsNextIsUpcoming_DoesNotSearchTheLeague()
    {
        SportsDbClient client = ClientFor(
            _ => Ok("""{"events":[{"idEvent":"9","strTimestamp":"2026-10-12T21:00:00","idHomeTeam":"135274","idLeague":"4424","strStatus":"NS"}]}"""),
            out FakeHandler handler);

        var next = await client.NextGameAsync("135274");

        Assert.AreEqual("9", next?.Id);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task NextGame_WhenNothingScheduled_ReturnsNull()
    {
        SportsDbClient client = ClientFor(_ => Ok("""{"events":null}"""), out FakeHandler handler);

        Assert.IsNull(await client.NextGameAsync("135274"));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task LastGame_PrefersFinishedGameStillListedAsNext()
    {
        SportsDbClient client = ClientFor(Route, out _);

        var last = await client.LastGameAsync("135274");

        Assert.AreEqual("2616723", last?.Id);
    }

    [TestMethod]
    public async Task Cloudflare1015_ThrowsRateLimited()
    {
        SportsDbClient client = new(new HttpClient(new FakeHandler(_ => Ok("error code: 1015"))));
        await Assert.ThrowsExactlyAsync<SportsDbRateLimitedException>(() => client.SearchTeamsAsync("Arsenal"));
    }

    /// <summary>Holds every request until the test releases it, and records whether it was cancelled.</summary>
    private sealed class PendingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                Finished.TrySetResult(false);
                return Ok("{}");
            }
            catch (OperationCanceledException)
            {
                Finished.TrySetResult(true);
                throw;
            }
        }
    }

    [TestMethod]
    public async Task SharedFetch_WhenEveryCallerLeaves_IsCancelled()
    {
        PendingHandler handler = new();
        SportsDbClient client = new(new HttpClient(handler));
        using CancellationTokenSource first = new();
        using CancellationTokenSource second = new();

        Task a = client.SearchTeamsAsync("Arsenal", first.Token);
        Task b = client.SearchTeamsAsync("Arsenal", second.Token);
        first.Cancel();
        second.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => a);
        await Assert.ThrowsAsync<OperationCanceledException>(() => b);
        Assert.IsTrue(await handler.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task SharedFetch_WhenOneCallerStays_Completes()
    {
        PendingHandler handler = new();
        SportsDbClient client = new(new HttpClient(handler));
        using CancellationTokenSource leaving = new();

        Task a = client.SearchTeamsAsync("Arsenal", leaving.Token);
        Task<IReadOnlyList<TraySport.Core.Models.Team>> b = client.SearchTeamsAsync("Arsenal");
        leaving.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => a);
        handler.Release.SetResult();

        Assert.AreEqual(0, (await b).Count);
        Assert.IsFalse(await handler.Finished.Task);
    }

    private sealed class MovableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public async Task PruneExpired_KeepsFreshResponses()
    {
        FakeHandler handler = new(_ => Ok("""{"teams":null}"""));
        MovableTime time = new(new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero));
        SportsDbClient client = new(new HttpClient(handler), time: time);

        await client.SearchTeamsAsync("Arsenal");            // 1 h freshness
        await client.LastGameAsync("1");                     // 5 min freshness, 2 requests
        time.Now += TimeSpan.FromMinutes(10);
        client.PruneExpired();
        await client.SearchTeamsAsync("Arsenal");
        await client.LastGameAsync("1");

        Assert.AreEqual(5, handler.Calls);
    }

    [TestMethod]
    public void ApiKey_DefaultsToFreeTier()
    {
        Assert.IsTrue(new SportsDbClient(new HttpClient()).IsFreeTier);
        Assert.IsFalse(new SportsDbClient(new HttpClient(), "abc123").IsFreeTier);
    }
}
