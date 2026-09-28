using System.Globalization;
using System.Net.Http;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Text;
using TraySport.Core.Api;
using TraySport.Core.Formatting;
using TraySport.Core.Models;
using TraySport.Services;

namespace TraySport.Views;

/// <summary>A team's details: last and next game with a recap link, standings, links, and about.</summary>
public sealed partial class TeamView : UserControl, IFlyoutView, IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private readonly TrayFlyoutWindow _host;
    private FavoriteTeam _team;
    private Team? _details;
    private CancellationTokenSource? _load;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    internal TeamView(TrayFlyoutWindow host, FavoriteTeam team)
    {
        InitializeComponent();
        _host = host;
        _team = team;

        TitleText.Text = team.Name;
        ToolTipService.SetToolTip(TitleText, team.Name);
        LeagueText.Text = team.League ?? team.Sport;
        BadgeImage.Source = Images.Badge(ImageUrls.Small(team.BadgeUrl), 64);
        AutomationProperties.SetName(BadgeImage, $"{team.Name} logo");
        BuildLinks();
        UpdateStar();
    }

    public void OnShown()
    {
        UpdateStar();
        if (DateTimeOffset.UtcNow - _loadedAt > StaleAfter)
        {
            _ = LoadAsync();
        }

        FocusWhenLoaded.Focus(BackButton);
    }

    public void OnHidden() => _load?.Cancel();

    public void Dispose() => _load?.Cancel();

    private async Task LoadAsync()
    {
        _load?.Cancel();
        using CancellationTokenSource cts = new();
        _load = cts;
        CancellationToken ct = cts.Token;
        SportsDbClient client = SportsData.Client;

        LoadingRing.IsActive = true;
        StatusBar.IsOpen = false;
        Exception? firstError = null;
        try
        {
            try
            {
                if (await client.LookupTeamAsync(_team.Id, ct) is { } details)
                {
                    ShowDetails(details);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                firstError ??= ex;
            }

            Game? last = null;
            Game? next = null;
            try
            {
                Task<Game?> lastTask = client.LastGameAsync(_team.Id, ct: ct);
                Task<Game?> nextTask = client.NextGameAsync(_team.Id, ct: ct);
                last = await lastTask;
                next = await nextTask;
                ShowGames(last, next);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                firstError ??= ex;
            }

            try
            {
                await LoadStandingsAsync(client, last, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                firstError ??= ex;
            }

            if (firstError is null)
            {
                _loadedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                ShowError(firstError);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Hidden or left the page.
        }
        finally
        {
            if (_load == cts)
            {
                _load = null;
                LoadingRing.IsActive = false;
            }
        }
    }

    private void ShowDetails(Team details)
    {
        _details = details;
        TitleText.Text = details.Name;
        LeagueText.Text = details.League ?? details.Sport;

        List<string> meta = [];
        if (details.FormedYear is { } year)
        {
            meta.Add($"Est. {year}");
        }

        if (details.Stadium is { } stadium)
        {
            meta.Add(stadium);
        }

        if (details.Location is { } location)
        {
            meta.Add(location);
        }

        MetaText.Text = string.Join(" · ", meta);

        if (details.Description is { } about)
        {
            AboutText.Text = about.Replace("\r\n\r\n", "\n\n", StringComparison.Ordinal);
            AboutSection.Visibility = Visibility.Visible;
        }

        BuildLinks();

        // Keep the saved favorite current (a renamed team, a new badge).
        if (FavoritesService.IsFavorite(details.Id))
        {
            FavoriteTeam fresh = FavoriteTeam.From(details);
            if (fresh != _team)
            {
                FavoritesService.Add(fresh);
            }

            _team = fresh;
        }
    }

    private void ShowGames(Game? last, Game? next)
    {
        LastHeader.Visibility = LastGame.Visibility = last is null ? Visibility.Collapsed : Visibility.Visible;
        NextHeader.Visibility = NextGame.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
        NoGamesText.Visibility = last is null && next is null ? Visibility.Visible : Visibility.Collapsed;
        if (last is not null)
        {
            LastGame.Show(last, _team.Id);
        }

        if (next is not null)
        {
            NextGame.Show(next, _team.Id);
        }
    }

    private async Task LoadStandingsAsync(SportsDbClient client, Game? last, CancellationToken ct)
    {
        string? leagueId = _details?.LeagueId ?? _team.LeagueId;
        if (leagueId is null)
        {
            return;
        }

        string? season = (await client.LookupLeagueAsync(leagueId, ct))?.CurrentSeason;
        IReadOnlyList<StandingRow> rows = await client.StandingsAsync(leagueId, season, ct: ct);

        // Between seasons the new table is empty; the season of the last game still has one.
        if (rows.Count == 0 && last?.Season is { } lastSeason && lastSeason != season && last.LeagueId == leagueId)
        {
            season = lastSeason;
            rows = await client.StandingsAsync(leagueId, season, ct: ct);
        }

        ShowStandings(rows, season, client.IsFreeTier);
    }

    private void ShowStandings(IReadOnlyList<StandingRow> rows, string? season, bool freeTier)
    {
        StandingsGrid.Children.Clear();
        StandingsGrid.RowDefinitions.Clear();
        StandingsGrid.ColumnDefinitions.Clear();
        if (rows.Count == 0)
        {
            StandingsSection.Visibility = Visibility.Collapsed;
            return;
        }

        bool hasDraws = rows.Any(r => r.Draws > 0);
        bool hasPoints = rows.Any(r => r.Points > 0);
        foreach (GridLength width in new[] { new GridLength(22), new GridLength(20), new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
        {
            StandingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        StandingsGrid.ColumnSpacing = 8;
        AddRow(0, "#", null, "Team", hasDraws ? "W-D-L" : "W-L", hasPoints ? "Pts" : "", header: true, highlight: false);
        int r = 1;
        foreach (StandingRow row in rows)
        {
            string record = hasDraws ? $"{row.Wins}-{row.Draws}-{row.Losses}" : $"{row.Wins}-{row.Losses}";
            AddRow(r++, row.Rank.ToString(CultureInfo.CurrentCulture), ImageUrls.Tiny(row.BadgeUrl), row.Team, record,
                hasPoints ? row.Points?.ToString(CultureInfo.CurrentCulture) ?? "" : "", header: false, highlight: row.TeamId == _team.Id);
        }

        string seasonText = season is null ? "" : $"{season} season. ";
        bool listed = rows.Any(row => row.TeamId == _team.Id);
        StandingsNote.Text = seasonText + (freeTier
            ? listed ? "The free data plan shows the top 5." : $"{_team.Name} is outside the top 5, which is all the free data plan shows."
            : "");
        StandingsNote.Visibility = StandingsNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StandingsSection.Visibility = Visibility.Visible;
    }

    private void AddRow(int row, string rank, string? badgeUrl, string team, string record, string points, bool header, bool highlight)
    {
        StandingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Style? caption = header ? (Style)Application.Current.Resources["SecondaryCaptionTextStyle"] : null;
        FontWeight weight = highlight ? FontWeights.SemiBold : FontWeights.Normal;

        void Add(FrameworkElement element, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            StandingsGrid.Children.Add(element);
        }

        TextBlock Text(string text, TextAlignment alignment = TextAlignment.Left) => new()
        {
            Text = text,
            Style = caption,
            FontWeight = weight,
            TextAlignment = alignment,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Add(Text(rank), 0);
        if (badgeUrl is not null)
        {
            Add(new Image { Width = 20, Height = 20, Source = Images.Badge(badgeUrl, 20) }, 1);
        }

        Add(Text(team), 2);
        Add(Text(record, TextAlignment.Right), 3);
        Add(Text(points, TextAlignment.Right), 4);
    }

    private void BuildLinks()
    {
        LinksPanel.Children.Clear();
        AddLink("", "Highlights on YouTube", GameFormatter.TeamHighlightsUrl(_details ?? ToTeam(_team)));
        if (_details?.YouTube is { } channel)
        {
            AddLink("", "Team YouTube channel", channel);
        }

        if (_details?.Website is { } site)
        {
            AddLink("", "Official website", site);
        }

        AddLink("", "More on TheSportsDB", SportsData.TeamPageUrl(_team.Id));
    }

    private static Team ToTeam(FavoriteTeam team) => new(team.Id, team.Name, team.Sport, team.League, team.LeagueId, team.BadgeUrl);

    private void AddLink(string glyph, string text, string url)
    {
        HyperlinkButton link = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 14 },
                    new TextBlock { Text = text },
                },
            },
        };
        AutomationProperties.SetName(link, text);
        ToolTipService.SetToolTip(link, url);
        link.Click += (_, _) => Links.Open(url);
        LinksPanel.Children.Add(link);
    }

    private void UpdateStar()
    {
        bool starred = FavoritesService.IsFavorite(_team.Id);
        StarIcon.Glyph = starred ? "" : "";
        string label = starred ? "Remove from favorites" : "Add to favorites";
        AutomationProperties.SetName(StarButton, label);
        ToolTipService.SetToolTip(StarButton, label);
    }

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesService.IsFavorite(_team.Id))
        {
            FavoritesService.Remove(_team.Id);
            TrayIconService.ForgetTeam(_team.Id);
        }
        else if (!FavoritesService.Add(_details is null ? _team : FavoriteTeam.From(_details)))
        {
            StatusBar.Title = "Favorites are full";
            StatusBar.Message = $"You can star up to {FavoritesPolicy.MaxFavorites} teams. Remove one first.";
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.IsOpen = true;
        }

        UpdateStar();
    }

    private void ShowError(Exception ex)
    {
        if (ex is SportsDbRateLimitedException)
        {
            StatusBar.Title = "Too many requests";
            StatusBar.Message = "TheSportsDB's free plan allows 30 a minute. Reopen this team in a minute.";
        }
        else
        {
            bool online = ConnectivityService.IsInternetAvailable;
            StatusBar.Title = online ? "Can't reach TheSportsDB" : "No internet connection";
            StatusBar.Message = ex is HttpRequestException or TaskCanceledException ? "Some details are missing. Try again in a moment." : ex.Message;
        }

        StatusBar.Severity = InfoBarSeverity.Warning;
        StatusBar.IsOpen = true;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        bool expanded = AboutText.MaxLines == 0;
        AboutText.MaxLines = expanded ? 5 : 0;
        MoreButton.Content = expanded ? "Show more" : "Show less";
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _host.GoBack();
}
