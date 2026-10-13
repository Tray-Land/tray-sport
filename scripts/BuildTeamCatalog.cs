// Builds Assets/teams.json: every team in the leagues Tray Sport lets you browse.
//
// The free TheSportsDB API lists only 10 teams per league and returns one result per name search,
// so the app ships this catalog to make browsing complete and let "Lakers" find Los Angeles. It's
// built from the same free API by searching each candidate name and keeping the teams whose current
// league matches. Candidates that moved league (relegated, promoted away) drop out on their own.
//
// Run from the repo root:  dotnet run scripts/BuildTeamCatalog.cs
// It stays under the free tier's 30 requests a minute, so a full run takes about 15 minutes.
// Rerun before a release, or when a new season changes a league's members.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

const string BaseUrl = "https://www.thesportsdb.com/api/v1/json/123/";
TimeSpan delay = TimeSpan.FromSeconds(2.6); // between requests: ~23 a minute

string root = Directory.GetCurrentDirectory();
string outPath = Path.Combine(root, "Assets", "teams.json");

// Finished leagues are saved here so an interrupted run resumes; delete it to start over.
string partialPath = Path.Combine(root, "scripts", ".catalog-partial.json");

if (!File.Exists(Path.Combine(root, "TraySport.csproj")))
{
    Console.Error.WriteLine("Run this from the repo root: dotnet run scripts/BuildTeamCatalog.cs");
    return 1;
}

// League id -> (league name as the API spells it, candidate team names).
(string Id, string Name, string[] Candidates)[] leagues =
[
    ("4391", "NFL", [
        "Arizona Cardinals", "Atlanta Falcons", "Baltimore Ravens", "Buffalo Bills", "Carolina Panthers",
        "Chicago Bears", "Cincinnati Bengals", "Cleveland Browns", "Dallas Cowboys", "Denver Broncos",
        "Detroit Lions", "Green Bay Packers", "Houston Texans", "Indianapolis Colts", "Jacksonville Jaguars",
        "Kansas City Chiefs", "Las Vegas Raiders", "Los Angeles Chargers", "Los Angeles Rams", "Miami Dolphins",
        "Minnesota Vikings", "New England Patriots", "New Orleans Saints", "New York Giants", "New York Jets",
        "Philadelphia Eagles", "Pittsburgh Steelers", "San Francisco 49ers", "Seattle Seahawks",
        "Tampa Bay Buccaneers", "Tennessee Titans", "Washington Commanders",
    ]),
    ("4424", "MLB", [
        "Arizona Diamondbacks", "Athletics", "Oakland Athletics", "Atlanta Braves", "Baltimore Orioles",
        "Boston Red Sox", "Chicago Cubs", "Chicago White Sox", "Cincinnati Reds", "Cleveland Guardians",
        "Colorado Rockies", "Detroit Tigers", "Houston Astros", "Kansas City Royals", "Los Angeles Angels",
        "Los Angeles Dodgers", "Miami Marlins", "Milwaukee Brewers", "Minnesota Twins", "New York Mets",
        "New York Yankees", "Philadelphia Phillies", "Pittsburgh Pirates", "San Diego Padres",
        "San Francisco Giants", "Seattle Mariners", "St. Louis Cardinals", "Tampa Bay Rays", "Texas Rangers",
        "Toronto Blue Jays", "Washington Nationals",
    ]),
    ("4387", "NBA", [
        "Atlanta Hawks", "Boston Celtics", "Brooklyn Nets", "Charlotte Hornets", "Chicago Bulls",
        "Cleveland Cavaliers", "Dallas Mavericks", "Denver Nuggets", "Detroit Pistons", "Golden State Warriors",
        "Houston Rockets", "Indiana Pacers", "Los Angeles Clippers", "Los Angeles Lakers", "Memphis Grizzlies",
        "Miami Heat", "Milwaukee Bucks", "Minnesota Timberwolves", "New Orleans Pelicans", "New York Knicks",
        "Oklahoma City Thunder", "Orlando Magic", "Philadelphia 76ers", "Phoenix Suns", "Portland Trail Blazers",
        "Sacramento Kings", "San Antonio Spurs", "Toronto Raptors", "Utah Jazz", "Washington Wizards",
    ]),
    ("4516", "WNBA", [
        "Atlanta Dream", "Chicago Sky", "Connecticut Sun", "Dallas Wings", "Golden State Valkyries",
        "Indiana Fever", "Las Vegas Aces", "Los Angeles Sparks", "Minnesota Lynx", "New York Liberty",
        "Phoenix Mercury", "Seattle Storm", "Washington Mystics", "Toronto Tempo", "Portland Fire",
    ]),
    ("4380", "NHL", [
        "Anaheim Ducks", "Boston Bruins", "Buffalo Sabres", "Calgary Flames", "Carolina Hurricanes",
        "Chicago Blackhawks", "Colorado Avalanche", "Columbus Blue Jackets", "Dallas Stars", "Detroit Red Wings",
        "Edmonton Oilers", "Florida Panthers", "Los Angeles Kings", "Minnesota Wild", "Montreal Canadiens",
        "Nashville Predators", "New Jersey Devils", "New York Islanders", "New York Rangers", "Ottawa Senators",
        "Philadelphia Flyers", "Pittsburgh Penguins", "San Jose Sharks", "Seattle Kraken", "St. Louis Blues",
        "Tampa Bay Lightning", "Toronto Maple Leafs", "Utah Mammoth", "Utah Hockey Club", "Vancouver Canucks",
        "Vegas Golden Knights", "Washington Capitals", "Winnipeg Jets",
    ]),
    ("4328", "English Premier League", [
        "Arsenal", "Aston Villa", "Bournemouth", "Brentford", "Brighton and Hove Albion", "Burnley", "Chelsea",
        "Crystal Palace", "Everton", "Fulham", "Leeds United", "Liverpool", "Manchester City",
        "Manchester United", "Newcastle United", "Nottingham Forest", "Sunderland", "Tottenham Hotspur",
        "West Ham United", "Wolverhampton Wanderers", "Ipswich Town", "Leicester City", "Southampton",
        "Sheffield United", "Coventry City", "Middlesbrough", "Norwich City", "Hull City", "Birmingham City",
        "West Bromwich Albion", "Millwall", "Bristol City", "Watford", "Stoke City", "Swansea City",
    ]),
    ("4346", "American Major League Soccer", [
        "Atlanta United", "Austin FC", "CF Montreal", "Charlotte FC", "Chicago Fire", "Colorado Rapids",
        "Columbus Crew", "DC United", "FC Cincinnati", "FC Dallas", "Houston Dynamo", "Inter Miami",
        "LA Galaxy", "Los Angeles FC", "Minnesota United", "Nashville SC", "New England Revolution",
        "New York City FC", "New York Red Bulls", "Orlando City", "Philadelphia Union", "Portland Timbers",
        "Real Salt Lake", "San Diego FC", "San Jose Earthquakes", "Seattle Sounders", "Sporting Kansas City",
        "St. Louis City", "Toronto FC", "Vancouver Whitecaps",
    ]),
    ("4335", "Spanish La Liga", [
        "Alaves", "Athletic Bilbao", "Atletico Madrid", "Barcelona", "Celta Vigo", "Elche", "Espanyol",
        "Getafe", "Girona", "Levante", "Mallorca", "Osasuna", "Rayo Vallecano", "Real Betis", "Real Madrid",
        "Real Oviedo", "Real Sociedad", "Sevilla", "Valencia", "Villarreal", "Las Palmas", "Leganes",
        "Real Valladolid", "Racing Santander", "Deportivo La Coruna", "Almeria", "Cadiz", "Granada",
        "Sporting Gijon", "Malaga", "Eibar", "Real Zaragoza",
    ]),
    ("4331", "German Bundesliga", [
        "Augsburg", "Bayer Leverkusen", "Bayern Munich", "Borussia Dortmund", "Borussia Monchengladbach",
        "Eintracht Frankfurt", "FC Koln", "Freiburg", "Hamburger SV", "Heidenheim", "Hoffenheim",
        "Mainz", "RB Leipzig", "St. Pauli", "Union Berlin", "Werder Bremen", "Wolfsburg", "VfB Stuttgart",
        "Bochum", "Holstein Kiel", "Schalke 04", "Hertha Berlin", "Darmstadt", "Hannover 96", "Paderborn",
        "Elversberg", "Karlsruher SC", "Fortuna Dusseldorf", "Kaiserslautern", "Nurnberg",
    ]),
    ("4332", "Italian Serie A", [
        "AC Milan", "Atalanta", "Bologna", "Cagliari", "Como", "Cremonese", "Fiorentina", "Genoa",
        "Hellas Verona", "Inter Milan", "Juventus", "Lazio", "Lecce", "Napoli", "Parma", "Pisa", "Roma",
        "Sassuolo", "Torino", "Udinese", "Empoli", "Monza", "Venezia", "Frosinone", "Salernitana",
        "Sampdoria", "Palermo", "Spezia", "Bari", "Modena", "Cesena", "Catanzaro",
    ]),
    ("4334", "French Ligue 1", [
        "Angers", "Auxerre", "Brest", "Le Havre", "Lens", "Lille", "Lorient", "Lyon", "Marseille", "Metz",
        "Monaco", "Nantes", "Nice", "Paris FC", "Rennes", "Strasbourg", "Toulouse",
        "Montpellier", "Reims", "Saint-Etienne", "Troyes", "Guingamp", "Dunkerque", "Clermont Foot",
        "Caen", "Bordeaux", "Ajaccio", "Red Star", "Le Mans",
    ]),
];

// Teams whose name search lands on another team ("Nottingham Forest" finds a netball club).
Dictionary<string, string[]> pinnedIds = new()
{
    ["4424"] = ["135280"], // St. Louis Cardinals
    ["4328"] = ["133720"], // Nottingham Forest
    ["4334"] = ["133714"], // Paris Saint-Germain
};

JsonSerializerOptions compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("TraySport-catalog-builder");

JsonObject done = File.Exists(partialPath)
    ? JsonNode.Parse(File.ReadAllText(partialPath))!.AsObject()
    : [];
Dictionary<string, JsonObject> teams = [];

foreach ((string leagueId, string leagueName, string[] candidates) in leagues)
{
    if (done[leagueId] is JsonArray saved)
    {
        Console.Error.WriteLine($"{leagueName}: {saved.Count} teams (saved)");
        foreach (JsonObject t in saved.OfType<JsonObject>())
        {
            teams[(string)t["id"]!] = (JsonObject)t.DeepClone();
        }

        continue;
    }

    Console.Error.WriteLine(leagueName);
    Dictionary<string, JsonObject> found = [];
    foreach (JsonObject t in Teams(await GetAsync("search_all_teams.php?l=" + Uri.EscapeDataString(leagueName))))
    {
        if (Str(t, "idLeague") == leagueId)
        {
            found[Str(t, "idTeam")!] = Slim(t);
        }
    }

    foreach (string teamId in pinnedIds.GetValueOrDefault(leagueId, []))
    {
        foreach (JsonObject t in Teams(await GetAsync("lookupteam.php?id=" + teamId)))
        {
            if (Str(t, "idLeague") == leagueId)
            {
                found[Str(t, "idTeam")!] = Slim(t);
            }
        }
    }

    HashSet<string> names = [.. found.Values.Select(v => ((string)v["name"]!).ToLowerInvariant())];
    foreach (string name in candidates)
    {
        if (names.Contains(name.ToLowerInvariant()))
        {
            continue;
        }

        List<JsonObject> hits = Teams(await GetAsync("searchteams.php?t=" + Uri.EscapeDataString(name)));
        if (hits.FirstOrDefault(t => Str(t, "idLeague") == leagueId) is { } hit)
        {
            found[Str(hit, "idTeam")!] = Slim(hit);
            names.Add(Str(hit, "strTeam")!.ToLowerInvariant());
        }
        else
        {
            string other = hits.Count > 0 ? $"{Str(hits[0], "strTeam")} / {Str(hits[0], "strLeague") ?? "?"}" : "no match";
            Console.Error.WriteLine($"  skip {name}: {other}");
        }
    }

    Console.Error.WriteLine($"  {found.Count} teams");
    foreach ((string id, JsonObject t) in found)
    {
        teams[id] = t;
    }

    done[leagueId] = new JsonArray([.. found.Values.Select(t => (JsonNode)t.DeepClone())]);
    File.WriteAllText(partialPath, done.ToJsonString(compact));
}

JsonObject output = new()
{
    ["generated"] = DateTime.Today.ToString("yyyy-MM-dd"),
    ["teams"] = new JsonArray([.. teams.Values
        .OrderBy(t => Str(t, "leagueId") ?? "", StringComparer.Ordinal)
        .ThenBy(t => Str(t, "name"), StringComparer.Ordinal)
        .Select(t => (JsonNode)t)]),
};
File.WriteAllText(outPath, output.ToJsonString(compact));
File.Delete(partialPath);
Console.Error.WriteLine($"Wrote {teams.Count} teams to {outPath}");
return 0;

async Task<JsonObject> GetAsync(string path)
{
    for (int attempt = 0; ; attempt++)
    {
        await Task.Delay(delay);
        string body;
        try
        {
            body = await http.GetStringAsync(BaseUrl + path); // a 429 throws
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            body = ex.Message; // a 429, a timeout, or a dropped connection: wait and retry
        }

        if (body.TrimStart().StartsWith('{'))
        {
            return JsonNode.Parse(body)!.AsObject();
        }

        if (attempt >= 6)
        {
            throw new InvalidOperationException($"{path}: {body[..Math.Min(80, body.Length)]}");
        }

        Console.Error.WriteLine($"  rate limited, waiting 65 s ({body[..Math.Min(40, body.Length)]})");
        await Task.Delay(TimeSpan.FromSeconds(65));
    }
}

static List<JsonObject> Teams(JsonObject response) =>
    response["teams"] is JsonArray list ? [.. list.OfType<JsonObject>()] : [];

// The API's values are strings or null; an empty string counts as missing.
static string? Str(JsonObject t, string key) =>
    t[key] is JsonValue v && v.TryGetValue(out string? s) && s.Length > 0 ? s : null;

static JsonObject Slim(JsonObject t) => new()
{
    ["id"] = Str(t, "idTeam"),
    ["name"] = Str(t, "strTeam"),
    ["short"] = Str(t, "strTeamShort"),
    ["alt"] = Str(t, "strTeamAlternate"),
    ["sport"] = Str(t, "strSport") ?? "",
    ["league"] = Str(t, "strLeague"),
    ["leagueId"] = Str(t, "idLeague"),
    ["badge"] = Str(t, "strBadge"),
    ["country"] = Str(t, "strCountry"),
};
