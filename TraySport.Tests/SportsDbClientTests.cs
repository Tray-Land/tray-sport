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

        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task Cloudflare1015_ThrowsRateLimited()
    {
        SportsDbClient client = new(new HttpClient(new FakeHandler(_ => Ok("error code: 1015"))));
        await Assert.ThrowsExactlyAsync<SportsDbRateLimitedException>(() => client.SearchTeamsAsync("Arsenal"));
    }

    [TestMethod]
    public void ApiKey_DefaultsToFreeTier()
    {
        Assert.IsTrue(new SportsDbClient(new HttpClient()).IsFreeTier);
        Assert.IsFalse(new SportsDbClient(new HttpClient(), "abc123").IsFreeTier);
    }
}
