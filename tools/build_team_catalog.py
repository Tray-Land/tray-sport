"""
Builds Assets/teams.json: every team in the leagues Tray Sport lets you browse.

The free TheSportsDB API lists only 10 teams per league and returns one result per name search,
so the app ships this catalog to make browsing complete and let "Lakers" find Los Angeles. It's
built from the same free API by searching each candidate name and keeping the teams whose current
league matches. Candidates that moved league (relegated, promoted away) drop out on their own.

Run from the repo root:  python tools/build_team_catalog.py
It stays under the free tier's 30 requests a minute, so a full run takes about 15 minutes.
Rerun before a release, or when a new season changes a league's members.
"""
import datetime
import json
import pathlib
import sys
import time
import urllib.parse
import urllib.request

BASE = "https://www.thesportsdb.com/api/v1/json/123/"
OUT = pathlib.Path(__file__).resolve().parent.parent / "Assets" / "teams.json"
# Finished leagues are saved here so an interrupted run resumes; delete it to start over.
PARTIAL = pathlib.Path(__file__).resolve().parent / ".catalog-partial.json"
DELAY = 2.6  # seconds between requests: ~23 a minute

# League id -> (league name as the API spells it, candidate team names).
LEAGUES = {
    "4391": ("NFL", [
        "Arizona Cardinals", "Atlanta Falcons", "Baltimore Ravens", "Buffalo Bills", "Carolina Panthers",
        "Chicago Bears", "Cincinnati Bengals", "Cleveland Browns", "Dallas Cowboys", "Denver Broncos",
        "Detroit Lions", "Green Bay Packers", "Houston Texans", "Indianapolis Colts", "Jacksonville Jaguars",
        "Kansas City Chiefs", "Las Vegas Raiders", "Los Angeles Chargers", "Los Angeles Rams", "Miami Dolphins",
        "Minnesota Vikings", "New England Patriots", "New Orleans Saints", "New York Giants", "New York Jets",
        "Philadelphia Eagles", "Pittsburgh Steelers", "San Francisco 49ers", "Seattle Seahawks",
        "Tampa Bay Buccaneers", "Tennessee Titans", "Washington Commanders",
    ]),
    "4424": ("MLB", [
        "Arizona Diamondbacks", "Athletics", "Oakland Athletics", "Atlanta Braves", "Baltimore Orioles",
        "Boston Red Sox", "Chicago Cubs", "Chicago White Sox", "Cincinnati Reds", "Cleveland Guardians",
        "Colorado Rockies", "Detroit Tigers", "Houston Astros", "Kansas City Royals", "Los Angeles Angels",
        "Los Angeles Dodgers", "Miami Marlins", "Milwaukee Brewers", "Minnesota Twins", "New York Mets",
        "New York Yankees", "Philadelphia Phillies", "Pittsburgh Pirates", "San Diego Padres",
        "San Francisco Giants", "Seattle Mariners", "St. Louis Cardinals", "Tampa Bay Rays", "Texas Rangers",
        "Toronto Blue Jays", "Washington Nationals",
    ]),
    "4387": ("NBA", [
        "Atlanta Hawks", "Boston Celtics", "Brooklyn Nets", "Charlotte Hornets", "Chicago Bulls",
        "Cleveland Cavaliers", "Dallas Mavericks", "Denver Nuggets", "Detroit Pistons", "Golden State Warriors",
        "Houston Rockets", "Indiana Pacers", "Los Angeles Clippers", "Los Angeles Lakers", "Memphis Grizzlies",
        "Miami Heat", "Milwaukee Bucks", "Minnesota Timberwolves", "New Orleans Pelicans", "New York Knicks",
        "Oklahoma City Thunder", "Orlando Magic", "Philadelphia 76ers", "Phoenix Suns", "Portland Trail Blazers",
        "Sacramento Kings", "San Antonio Spurs", "Toronto Raptors", "Utah Jazz", "Washington Wizards",
    ]),
    "4516": ("WNBA", [
        "Atlanta Dream", "Chicago Sky", "Connecticut Sun", "Dallas Wings", "Golden State Valkyries",
        "Indiana Fever", "Las Vegas Aces", "Los Angeles Sparks", "Minnesota Lynx", "New York Liberty",
        "Phoenix Mercury", "Seattle Storm", "Washington Mystics", "Toronto Tempo", "Portland Fire",
    ]),
    "4380": ("NHL", [
        "Anaheim Ducks", "Boston Bruins", "Buffalo Sabres", "Calgary Flames", "Carolina Hurricanes",
        "Chicago Blackhawks", "Colorado Avalanche", "Columbus Blue Jackets", "Dallas Stars", "Detroit Red Wings",
        "Edmonton Oilers", "Florida Panthers", "Los Angeles Kings", "Minnesota Wild", "Montreal Canadiens",
        "Nashville Predators", "New Jersey Devils", "New York Islanders", "New York Rangers", "Ottawa Senators",
        "Philadelphia Flyers", "Pittsburgh Penguins", "San Jose Sharks", "Seattle Kraken", "St. Louis Blues",
        "Tampa Bay Lightning", "Toronto Maple Leafs", "Utah Mammoth", "Utah Hockey Club", "Vancouver Canucks",
        "Vegas Golden Knights", "Washington Capitals", "Winnipeg Jets",
    ]),
    "4328": ("English Premier League", [
        "Arsenal", "Aston Villa", "Bournemouth", "Brentford", "Brighton and Hove Albion", "Burnley", "Chelsea",
        "Crystal Palace", "Everton", "Fulham", "Leeds United", "Liverpool", "Manchester City",
        "Manchester United", "Newcastle United", "Nottingham Forest", "Sunderland", "Tottenham Hotspur",
        "West Ham United", "Wolverhampton Wanderers", "Ipswich Town", "Leicester City", "Southampton",
        "Sheffield United", "Coventry City", "Middlesbrough", "Norwich City", "Hull City", "Birmingham City",
        "West Bromwich Albion", "Millwall", "Bristol City", "Watford", "Stoke City", "Swansea City",
    ]),
    "4346": ("American Major League Soccer", [
        "Atlanta United", "Austin FC", "CF Montreal", "Charlotte FC", "Chicago Fire", "Colorado Rapids",
        "Columbus Crew", "DC United", "FC Cincinnati", "FC Dallas", "Houston Dynamo", "Inter Miami",
        "LA Galaxy", "Los Angeles FC", "Minnesota United", "Nashville SC", "New England Revolution",
        "New York City FC", "New York Red Bulls", "Orlando City", "Philadelphia Union", "Portland Timbers",
        "Real Salt Lake", "San Diego FC", "San Jose Earthquakes", "Seattle Sounders", "Sporting Kansas City",
        "St. Louis City", "Toronto FC", "Vancouver Whitecaps",
    ]),
    "4335": ("Spanish La Liga", [
        "Alaves", "Athletic Bilbao", "Atletico Madrid", "Barcelona", "Celta Vigo", "Elche", "Espanyol",
        "Getafe", "Girona", "Levante", "Mallorca", "Osasuna", "Rayo Vallecano", "Real Betis", "Real Madrid",
        "Real Oviedo", "Real Sociedad", "Sevilla", "Valencia", "Villarreal", "Las Palmas", "Leganes",
        "Real Valladolid", "Racing Santander", "Deportivo La Coruna", "Almeria", "Cadiz", "Granada",
        "Sporting Gijon", "Malaga", "Eibar", "Real Zaragoza",
    ]),
    "4331": ("German Bundesliga", [
        "Augsburg", "Bayer Leverkusen", "Bayern Munich", "Borussia Dortmund", "Borussia Monchengladbach",
        "Eintracht Frankfurt", "FC Koln", "Freiburg", "Hamburger SV", "Heidenheim", "Hoffenheim",
        "Mainz", "RB Leipzig", "St. Pauli", "Union Berlin", "Werder Bremen", "Wolfsburg", "VfB Stuttgart",
        "Bochum", "Holstein Kiel", "Schalke 04", "Hertha Berlin", "Darmstadt", "Hannover 96", "Paderborn",
        "Elversberg", "Karlsruher SC", "Fortuna Dusseldorf", "Kaiserslautern", "Nurnberg",
    ]),
    "4332": ("Italian Serie A", [
        "AC Milan", "Atalanta", "Bologna", "Cagliari", "Como", "Cremonese", "Fiorentina", "Genoa",
        "Hellas Verona", "Inter Milan", "Juventus", "Lazio", "Lecce", "Napoli", "Parma", "Pisa", "Roma",
        "Sassuolo", "Torino", "Udinese", "Empoli", "Monza", "Venezia", "Frosinone", "Salernitana",
        "Sampdoria", "Palermo", "Spezia", "Bari", "Modena", "Cesena", "Catanzaro",
    ]),
    "4334": ("French Ligue 1", [
        "Angers", "Auxerre", "Brest", "Le Havre", "Lens", "Lille", "Lorient", "Lyon", "Marseille", "Metz",
        "Monaco", "Nantes", "Nice", "Paris FC", "Rennes", "Strasbourg", "Toulouse",
        "Montpellier", "Reims", "Saint-Etienne", "Troyes", "Guingamp", "Dunkerque", "Clermont Foot",
        "Caen", "Bordeaux", "Ajaccio", "Red Star", "Le Mans",
    ]),
}

# Teams whose name search lands on another team ("Nottingham Forest" finds a netball club).
PINNED_IDS = {
    "4424": ["135280"],  # St. Louis Cardinals
    "4328": ["133720"],  # Nottingham Forest
    "4334": ["133714"],  # Paris Saint-Germain
}


def get(path, attempt=0):
    time.sleep(DELAY)
    req = urllib.request.Request(BASE + path, headers={"User-Agent": "TraySport catalog builder"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            body = r.read().decode("utf-8")
    except Exception as ex:  # 429 raises HTTPError
        body = str(ex)
    if not body.lstrip().startswith("{"):
        if attempt >= 6:
            raise RuntimeError(f"{path}: {body[:80]}")
        print(f"  rate limited, waiting 65 s ({body[:40]!r})", file=sys.stderr)
        time.sleep(65)
        return get(path, attempt + 1)
    return json.loads(body)


def slim(t):
    return {
        "id": t["idTeam"],
        "name": t["strTeam"],
        "short": t.get("strTeamShort") or None,
        "alt": t.get("strTeamAlternate") or None,
        "sport": t.get("strSport") or "",
        "league": t.get("strLeague") or None,
        "leagueId": t.get("idLeague") or None,
        "badge": t.get("strBadge") or None,
        "country": t.get("strCountry") or None,
    }


def main():
    done = json.loads(PARTIAL.read_text(encoding="utf-8")) if PARTIAL.exists() else {}
    teams = {}
    for league_id, (league_name, candidates) in LEAGUES.items():
        if league_id in done:
            print(f"{league_name}: {len(done[league_id])} teams (saved)", file=sys.stderr)
            teams.update({t["id"]: t for t in done[league_id]})
            continue
        print(f"{league_name}", file=sys.stderr)
        found = {}
        for t in get("search_all_teams.php?l=" + urllib.parse.quote(league_name)).get("teams") or []:
            if t.get("idLeague") == league_id:
                found[t["idTeam"]] = slim(t)
        for team_id in PINNED_IDS.get(league_id, []):
            for t in get("lookupteam.php?id=" + team_id).get("teams") or []:
                if t.get("idLeague") == league_id:
                    found[t["idTeam"]] = slim(t)
        names = {v["name"].lower() for v in found.values()}
        for name in candidates:
            if name.lower() in names:
                continue
            hits = get("searchteams.php?t=" + urllib.parse.quote(name)).get("teams") or []
            hit = next((t for t in hits if t.get("idLeague") == league_id), None)
            if hit:
                found[hit["idTeam"]] = slim(hit)
                names.add(hit["strTeam"].lower())
            else:
                other = hits[0]["strTeam"] + " / " + (hits[0].get("strLeague") or "?") if hits else "no match"
                print(f"  skip {name}: {other}", file=sys.stderr)
        print(f"  {len(found)} teams", file=sys.stderr)
        teams.update(found)
        done[league_id] = list(found.values())
        PARTIAL.write_text(json.dumps(done), encoding="utf-8")

    OUT.write_text(json.dumps({
        "generated": datetime.date.today().isoformat(),
        "teams": sorted(teams.values(), key=lambda t: (t["leagueId"] or "", t["name"])),
    }, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    PARTIAL.unlink(missing_ok=True)
    print(f"Wrote {len(teams)} teams to {OUT}", file=sys.stderr)


if __name__ == "__main__":
    main()
