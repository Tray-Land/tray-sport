using System.Globalization;
using System.Net;
using TraySport.Core.Models;

namespace TraySport.Core.Api;

/// <summary>The server rate-limited us (HTTP 429 or Cloudflare error 1015).</summary>
public sealed class SportsDbRateLimitedException(DateTimeOffset retryAt)
    : HttpRequestException("TheSportsDB is rate limiting requests.", null, HttpStatusCode.TooManyRequests)
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

/// <summary>
/// TheSportsDB v1 client. Every response is cached for a time that matches how fast it changes,
/// identical requests in flight are shared, and a <see cref="RequestGate"/> keeps the whole app
/// under the free tier's 30 requests a minute.
/// </summary>
/// <remarks>
/// Free-tier caps that shape the UI: team search returns 1 result, a league lists 10 teams, a
/// team's last and next schedules return 1 game each, and a league table returns the top 5.
/// </remarks>
public sealed class SportsDbClient
{
    public const string FreeApiKey = "123";

    public static readonly TimeSpan TeamTtl = TimeSpan.FromHours(12);
    public static readonly TimeSpan ScheduleTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TableTtl = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan SearchTtl = TimeSpan.FromHours(1);

    /// <summary>A forced refresh still reuses data this fresh, so the refresh button can't burn the quota.</summary>
    public static readonly TimeSpan MinRefreshAge = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RateLimitPenalty = TimeSpan.FromSeconds(65);

    /// <summary>How many UTC days (starting today) the stale-next fallback scans; covers playoff off-days.</summary>
    private const int NextGameSearchDays = 3;

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly RequestGate _gate;
    private readonly TimeProvider _time;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, (string Json, DateTimeOffset FetchedAt)> _cache = [];
    private readonly Dictionary<string, Task<string>> _inFlight = [];

    public SportsDbClient(HttpClient http, string? apiKey = null, RequestGate? gate = null, TimeProvider? time = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
        _baseUrl = $"https://www.thesportsdb.com/api/v1/json/{(string.IsNullOrWhiteSpace(apiKey) ? FreeApiKey : apiKey.Trim())}/";

        // A little under the documented 30/min: the limit is per IP and the clock edges are fuzzy.
        _gate = gate ?? new RequestGate(25, TimeSpan.FromMinutes(1), _time);
    }

    public bool IsFreeTier => _baseUrl.Contains($"/{FreeApiKey}/", StringComparison.Ordinal);

    public async Task<IReadOnlyList<Team>> SearchTeamsAsync(string name, CancellationToken ct = default) =>
        SportsDbMapper.ParseTeams(await GetAsync($"searchteams.php?t={Uri.EscapeDataString(name.Trim())}", SearchTtl, false, ct));

    public async Task<IReadOnlyList<Team>> ListTeamsAsync(string leagueName, CancellationToken ct = default) =>
        SportsDbMapper.ParseTeams(await GetAsync($"search_all_teams.php?l={Uri.EscapeDataString(leagueName)}", TeamTtl, false, ct));

    public async Task<Team?> LookupTeamAsync(string teamId, CancellationToken ct = default) =>
        SportsDbMapper.ParseTeams(await GetAsync($"lookupteam.php?id={Uri.EscapeDataString(teamId)}", TeamTtl, false, ct)).FirstOrDefault();

    /// <summary>
    /// The latest finished game. The free eventslast can lag behind, so a finished game that
    /// eventsnext still lists counts too (that call is shared with <see cref="NextGameAsync"/>).
    /// </summary>
    public async Task<Game?> LastGameAsync(string teamId, bool force = false, CancellationToken ct = default)
    {
        IReadOnlyList<Game> past = await TeamEventsAsync("eventslast.php", teamId, force, ct);
        IReadOnlyList<Game> upcoming = await TeamEventsAsync("eventsnext.php", teamId, force, ct);
        return GameSelector.PickLast(past, upcoming, teamId);
    }

    /// <summary>
    /// The next game that hasn't been played. The free eventsnext returns one game, and it can be a
    /// finished or long-past one; then the league's schedule for the coming days is searched instead.
    /// </summary>
    public async Task<Game?> NextGameAsync(string teamId, bool force = false, CancellationToken ct = default)
    {
        IReadOnlyList<Game> upcoming = await TeamEventsAsync("eventsnext.php", teamId, force, ct);
        DateTimeOffset now = _time.GetUtcNow();
        if (GameSelector.PickNext(upcoming, teamId, now) is { } next)
        {
            return next;
        }

        // Only a stale row can name the league to search; an empty answer (off-season) ends here.
        if (upcoming.FirstOrDefault(g => GameSelector.InvolvesTeam(g, teamId) && g.LeagueId is not null)?.LeagueId is not { } leagueId)
        {
            return null;
        }

        for (int day = 0; day < NextGameSearchDays; day++)
        {
            string date = now.AddDays(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            IReadOnlyList<Game> games = SportsDbMapper.ParseGames(
                await GetAsync($"eventsday.php?d={date}&l={Uri.EscapeDataString(leagueId)}", ScheduleTtl, force, ct));
            if (GameSelector.PickNext(games, teamId, now) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<Game>> TeamEventsAsync(string endpoint, string teamId, bool force, CancellationToken ct) =>
        SportsDbMapper.ParseGames(await GetAsync($"{endpoint}?id={Uri.EscapeDataString(teamId)}", ScheduleTtl, force, ct));

    public async Task<League?> LookupLeagueAsync(string leagueId, CancellationToken ct = default) =>
        SportsDbMapper.ParseLeagues(await GetAsync($"lookupleague.php?id={Uri.EscapeDataString(leagueId)}", TeamTtl, false, ct)).FirstOrDefault();

    public async Task<IReadOnlyList<StandingRow>> StandingsAsync(string leagueId, string? season, bool force = false, CancellationToken ct = default)
    {
        string query = $"lookuptable.php?l={Uri.EscapeDataString(leagueId)}";
        if (!string.IsNullOrWhiteSpace(season))
        {
            query += $"&s={Uri.EscapeDataString(season)}";
        }

        return SportsDbMapper.ParseTable(await GetAsync(query, TableTtl, force, ct));
    }

    /// <summary>Drops everything cached, e.g. when the app goes idle.</summary>
    public void ClearCache()
    {
        lock (_sync)
        {
            _cache.Clear();
        }
    }

    private async Task<string> GetAsync(string query, TimeSpan ttl, bool force, CancellationToken ct)
    {
        Task<string> fetch;
        lock (_sync)
        {
            DateTimeOffset now = _time.GetUtcNow();
            if (_cache.TryGetValue(query, out var hit) && now - hit.FetchedAt < (force ? MinRefreshAge : ttl))
            {
                return hit.Json;
            }

            if (!_inFlight.TryGetValue(query, out fetch!))
            {
                fetch = FetchAsync(query);
                _inFlight[query] = fetch;
            }
        }

        // The shared fetch isn't cancelled by one caller leaving; each caller stops waiting on its own token.
        return await fetch.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> FetchAsync(string query)
    {
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
            await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);

            using HttpResponseMessage response = await _http.GetAsync(_baseUrl + query, timeout.Token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (IsRateLimited(response.StatusCode, body))
            {
                DateTimeOffset retryAt = _time.GetUtcNow() + RateLimitPenalty;
                _gate.BlockUntil(retryAt);
                throw new SportsDbRateLimitedException(retryAt);
            }

            response.EnsureSuccessStatusCode();

            // Some lookups (e.g. a table the free plan doesn't cover) answer 200 with no body: no data, not an error.
            if (string.IsNullOrWhiteSpace(body))
            {
                body = "{}";
            }
            else if (!body.TrimStart().StartsWith('{'))
            {
                throw new HttpRequestException("TheSportsDB returned an unexpected response.");
            }

            lock (_sync)
            {
                _cache[query] = (body, _time.GetUtcNow());
            }

            return body;
        }
        finally
        {
            lock (_sync)
            {
                _inFlight.Remove(query);
            }
        }
    }

    /// <summary>Cloudflare answers an over-limit client with "error code: 1015", sometimes with a 200.</summary>
    public static bool IsRateLimited(HttpStatusCode status, string body) =>
        status == HttpStatusCode.TooManyRequests || body.TrimStart().StartsWith("error code: 1015", StringComparison.OrdinalIgnoreCase);
}
