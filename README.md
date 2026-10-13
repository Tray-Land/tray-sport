# Tray Sport

A tray-only Windows app that keeps your favorite teams' scores one click away. Pick a sport, find
your team, and star it. The flyout then shows each starred team's last result and next game, and
the tray tooltip summarizes the latest scores.

<img src="docs/flyout.png" alt="The Tray Sport flyout showing four favorite teams with their last result and next game" width="380">

## What it does

- **Favorites:** a card per starred team with its last result (W/L/D, score, opponent) and next game. Right-click a card to reorder it, remove it, or use its logo as the tray icon.
- **Find a team:** browse Football (NFL, NCAA), Baseball (MLB), Basketball (NBA, WNBA), Hockey (NHL), or Soccer (Premier League, MLS, La Liga, Bundesliga, Serie A, Ligue 1, Champions League), or search by name.
- **Team details:** last and next game, standings, links to highlights, the team's YouTube channel and website, and an About section.
- **Recaps:** each finished game links to its recap on YouTube. That's the event's own video when TheSportsDB has one, or otherwise a YouTube search for that game's highlights.
- **Tray icon:** a sport glyph (football, baseball, basketball, hockey, or soccer) that follows the taskbar's light or dark theme, or any starred team's logo in full color. Change it in Settings.

- **Tray clicks:** left-click toggles the flyout. Right-click opens a menu with Open, Find a team, Settings, and Exit. Exit is the only way to quit; closing the flyout just hides it.
- **Settings:** the tray icon, and Start with Windows (off by default).

Data comes from [TheSportsDB](https://www.thesportsdb.com).

## Install

Download the `.msix` for your PC (x64 or ARM64) from the latest [GitHub release](../../releases) and open it. The
package is signed with the publisher's certificate; if Windows says it can't verify the publisher, install that
certificate into **Local Machine > Trusted People** first.

Requires Windows 10 version 1809 or later, or Windows 11, on x64 or ARM64.

## Setup

Nothing to set up: the app uses TheSportsDB's free public key. If you have a premium key, copy
`OAuth.resw.sample` to `OAuth.resw`, set `SportsDbApiKey`, and rebuild (see below).

Favorites and preferences are stored in the app's local settings. Chosen team logos are cached as icons in the
app's `LocalFolder\TrayIcons`. Uninstalling removes both.

## Privacy and footprint

- No account, no sign-in, no telemetry. The only network calls go to TheSportsDB (`www.thesportsdb.com` and its
  image host).
- Scores refresh only while the flyout is open: every 5 minutes, backing off to every 10 minutes when TheSportsDB
  can't be reached. Closing the flyout cancels anything in flight. Hidden, the app makes no requests at all.
  The one exception is right after launch, when a chosen team logo is missing from the icon cache and has to be
  downloaded once.
- A minute after the flyout closes, the app releases its window and caches and drops back to a small idle footprint.
- The app stores no secrets. A premium API key, if you add one, is compiled into the package from `OAuth.resw`,
  which is never committed.

## Build from source

Prerequisites: the .NET 10 SDK, Windows Developer Mode, and the winapp CLI (`winget install Microsoft.WinAppCli`).

```powershell
winapp run            # build and launch with package identity; first run opens the flyout
winapp run --clean    # reset settings and favorites to test first run
dotnet test TraySport.Tests
```

To publish a release, bump `Identity/@Version` in `Package.appxmanifest`, then run `build-msix-for-gh.ps1`. It
builds and signs x64 and ARM64 packages and creates a draft GitHub release.

## TheSportsDB free plan

The app uses the public free key (`123`) unless `OAuth.resw` sets `SportsDbApiKey`. The free
plan's limits shape a few things:

| Limit | How the app handles it |
|---|---|
| 30 requests a minute | `RequestGate` holds requests to 25 a minute. On a 429 or Cloudflare `error code: 1015`, all requests pause for 65 s. Responses are cached by how fast they change: schedules for 5 min, tables for 30 min, teams for 12 h. |
| A league lists only 10 teams | `Assets\teams.json` bundles every team in the browsable leagues. |
| A name search returns one result | Search matches the bundled catalog as you type ("lakers" finds Los Angeles). Pressing Enter also adds TheSportsDB's single best match from any league. |
| One last game and one next game per team | Favorites and team pages show exactly one of each. |
| The one "next" game can be stale (already final, or long past) | If `eventsnext` returns a finished game, the app searches the team's league with `eventsday` for today and the next 2 days (UTC), up to 3 extra requests shared by every team in that league. The finished game counts as the last result when it is newer than `eventslast`. |
| Tables return the top 5 | Standings say so, and say when your team is below the cut. |
| Favorites refresh with 2 requests each | Favorites are capped at 10. Normally 2 requests each, more only when a team's next game is stale. |

A premium key in `OAuth.resw` lifts the caps with no code changes, apart from the notes in the UI that mention the free plan.

### Refreshing the team catalog

```powershell
dotnet run scripts/BuildTeamCatalog.cs
```

The script searches each candidate team name in `scripts/BuildTeamCatalog.cs` and keeps the teams
whose current league matches, so relegated or moved teams drop out on their own. Run it before a
release or when a new season starts, and add newly promoted teams to the candidate lists. It
stays under the rate limit, so a full run takes about 15 minutes.

## Icons

- `branding\icon.svg`: the app icon (soccer ball).
- `branding\sports\*.svg`: sport glyphs drawn in white. `branding\Build-SportIcons.ps1` renders them into `Assets\Sports\<sport>.ico` (white, for dark taskbars) and `<sport>-dark.ico` (black, for light taskbars).
- Team logos are rendered at runtime by `TrayIconService` from the badge's `/small` size into a multi-size `.ico` in `LocalFolder\TrayIcons`.

## Layout

```
App.xaml.cs                 tray icon, single instance, exit
Views/TrayFlyoutWindow      the popup and its page stack (favorites -> find / team / settings)
Views/FlyoutPage            favorites (home); polls only while visible
Views/FindTeamView          sport + league browse, search, star
Views/TeamView              team details, games, standings, links
Views/SettingsView          tray icon picker, start with Windows
Services/                   SportsData (shared client), Favorites, TrayIcon, TeamCatalog, ...
TraySport.Core/             API client, rate gate, parsing, formatting, ICO writer, catalog (no WinRT)
TraySport.Tests/            MSTest over Core, with recorded API responses
scripts/BuildTeamCatalog.cs C# file-based app that regenerates Assets\teams.json
```

## License

MIT, see [LICENSE](LICENSE).
