using System.Collections.ObjectModel;
using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraySport.Core.Api;
using TraySport.Core.Models;
using TraySport.Services;

namespace TraySport.Views;

/// <summary>
/// Pick a sport and league to browse its teams, or search any team by name, then star it.
/// </summary>
public sealed partial class FindTeamView : UserControl, IFlyoutView, IDisposable
{
    private readonly TrayFlyoutWindow _host;
    private readonly ObservableCollection<TeamRow> _rows = [];
    private CancellationTokenSource? _load;
    private int _generation;
    private bool _searching;
    private string? _loadedKey;

    internal FindTeamView(TrayFlyoutWindow host)
    {
        InitializeComponent();
        _host = host;
        TeamsList.ItemsSource = _rows;
        SportPicker.ItemsSource = SportCatalog.Sports;
        SportPicker.SelectedItem = SportCatalog.FindByIconKey(SettingsService.LastSport) ?? SportCatalog.Sports[0];
    }

    public void OnShown()
    {
        // Stars may have changed on a team page.
        foreach (TeamRow row in _rows)
        {
            row.Refresh();
        }

        if (_loadedKey is null)
        {
            _ = LoadLeagueAsync();
        }

        FocusWhenLoaded.Focus(SearchBox);
    }

    public void OnHidden()
    {
        _load?.Cancel();

        // A cancelled load left nothing behind; load again next time.
        if (LoadingRing.IsActive)
        {
            _loadedKey = null;
            LoadingRing.IsActive = false;
        }
    }

    public void Dispose() => _load?.Cancel();

    private LeagueInfo? SelectedLeague => LeaguePicker.SelectedItem as LeagueInfo;

    private void SportPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SportPicker.SelectedItem is not SportInfo sport)
        {
            return;
        }

        SettingsService.LastSport = sport.IconKey;
        LeaguePicker.ItemsSource = sport.Leagues;
        LeaguePicker.SelectedItem = sport.Leagues.FirstOrDefault(l => l.Id == SettingsService.LastLeagueId) ?? sport.Leagues[0];
    }

    private void LeaguePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedLeague is not { } league)
        {
            return;
        }

        SettingsService.LastLeagueId = league.Id;
        ClearSearch();
        if (_host.IsPopupVisible)
        {
            _ = LoadLeagueAsync();
        }
        else
        {
            _loadedKey = null;
        }
    }

    private async Task LoadLeagueAsync()
    {
        if (SelectedLeague is not { } league)
        {
            return;
        }

        _loadedKey = "league:" + league.Id;

        // The bundled catalog has every team in the league; the free API lists only ten.
        TeamCatalog catalog = TeamCatalogService.Catalog;
        bool bundled = catalog.HasLeague(league.Id);
        FootnoteText.Text = bundled ? "" : "The free data plan lists up to 10 teams per league. Search by name to find any team.";
        FootnoteText.Visibility = bundled ? Visibility.Collapsed : Visibility.Visible;
        await LoadAsync(
            bundled ? _ => Task.FromResult(catalog.TeamsIn(league.Id)) : ct => SportsData.Client.ListTeamsAsync(league.Name, ct),
            $"No teams found in {league.Name}.");
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = args.QueryText.Trim();
        if (query.Length < 2)
        {
            return;
        }

        // Enter also asks TheSportsDB, which knows teams outside the bundled leagues.
        _searching = true;
        _loadedKey = "search:" + query;
        ShowSearchFootnote("The free data plan adds its single best match from every league.");
        _ = LoadAsync(ct => SearchEverywhereAsync(query, ct), $"No team matches “{query}”.");
    }

    private static async Task<IReadOnlyList<Team>> SearchEverywhereAsync(string query, CancellationToken ct)
    {
        List<Team> teams = [.. TeamCatalogService.Catalog.Search(query)];
        try
        {
            foreach (Team team in await SportsData.Client.SearchTeamsAsync(query, ct))
            {
                if (teams.All(t => t.Id != team.Id))
                {
                    teams.Add(team);
                }
            }
        }
        catch (Exception) when (teams.Count > 0 && !ct.IsCancellationRequested)
        {
            // Offline or rate limited: the bundled matches are still worth showing.
        }

        return teams;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        string query = sender.Text.Trim();
        if (query.Length >= 2)
        {
            // Instant, offline matches from the bundled leagues as you type.
            _searching = true;
            _loadedKey = "local:" + query;
            ShowSearchFootnote("Press Enter to search every league on TheSportsDB.");
            _ = LoadAsync(_ => Task.FromResult(TeamCatalogService.Catalog.Search(query)), "No match in the major leagues. Press Enter to search everywhere.", keepOrder: true);
        }
        else if (_searching && query.Length == 0)
        {
            // Clearing the box goes back to browsing the league.
            ClearSearch();
            _ = LoadLeagueAsync();
        }
    }

    private void ShowSearchFootnote(string text)
    {
        FootnoteText.Text = text;
        FootnoteText.Visibility = Visibility.Visible;
    }

    private void ClearSearch()
    {
        _searching = false;
        SearchBox.Text = "";
    }

    private async Task LoadAsync(Func<CancellationToken, Task<IReadOnlyList<Team>>> fetch, string emptyMessage, bool keepOrder = false)
    {
        _load?.Cancel();
        CancellationTokenSource cts = new();
        _load = cts;
        int generation = ++_generation;

        _rows.Clear();
        EmptyText.Visibility = Visibility.Collapsed;
        StatusBar.IsOpen = false;
        LoadingRing.IsActive = true;
        try
        {
            IReadOnlyList<Team> teams = await fetch(cts.Token);
            if (generation != _generation)
            {
                return;
            }

            // League lists read alphabetically; search results keep their best-match order.
            foreach (Team team in keepOrder || _searching ? teams : teams.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).AsEnumerable())
            {
                _rows.Add(new TeamRow(team));
            }

            EmptyText.Text = emptyMessage;
            EmptyText.Visibility = teams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Replaced by a newer load, or the flyout hid.
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                _loadedKey = null;
                ShowError(ex);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                LoadingRing.IsActive = false;
            }
        }
    }

    private void ShowError(Exception ex)
    {
        if (ex is SportsDbRateLimitedException)
        {
            StatusBar.Title = "Too many requests";
            StatusBar.Message = "TheSportsDB's free plan allows 30 a minute. Try again in a minute.";
        }
        else
        {
            bool online = ConnectivityService.IsInternetAvailable;
            StatusBar.Title = online ? "Can't reach TheSportsDB" : "No internet connection";
            StatusBar.Message = ex is HttpRequestException or TaskCanceledException ? "Try again in a moment." : ex.Message;
        }

        StatusBar.Severity = InfoBarSeverity.Warning;
        StatusBar.IsOpen = true;
    }

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TeamRow row })
        {
            return;
        }

        if (row.IsFavorite)
        {
            FavoritesService.Remove(row.Team.Id);
            TrayIconService.ForgetTeam(row.Team.Id);
        }
        else if (!FavoritesService.Add(FavoriteTeam.From(row.Team)))
        {
            StatusBar.Title = "Favorites are full";
            StatusBar.Message = $"You can star up to {FavoritesPolicy.MaxFavorites} teams. Remove one first.";
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.IsOpen = true;
        }

        row.Refresh();
    }

    private void TeamsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TeamRow row)
        {
            _host.ShowTeam(FavoriteTeam.From(row.Team));
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _host.GoBack();
}
