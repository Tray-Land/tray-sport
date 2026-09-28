using System.Collections.ObjectModel;
using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TraySport.Core.Api;
using TraySport.Core.Formatting;
using TraySport.Core.Icons;
using TraySport.Core.Models;
using TraySport.Services;

namespace TraySport.Views;

/// <summary>
/// The flyout's home: a card per starred team with its last result and next game. Data refreshes
/// only while the flyout is on screen; see <see cref="ForegroundPoller"/>.
/// </summary>
public sealed partial class FlyoutPage : Page, IFlyoutView, IDisposable
{
    // Scores change a few times a day; the API's own cache is about as fresh as this.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly TrayFlyoutWindow _host;
    private readonly ForegroundPoller _poller;
    private readonly ObservableCollection<FavoriteCard> _cards = [];

    internal FlyoutPage(TrayFlyoutWindow host)
    {
        InitializeComponent();
        _host = host;
        TitleText.Text = App.DisplayName;
        FavoritesList.ItemsSource = _cards;
        _poller = new ForegroundPoller(DispatcherQueue, RefreshInterval, RefreshAsync);
        _poller.Failed += (_, ex) => ShowError(ex);
    }

    /// <summary>The flyout opened (or the user came back from another page): refresh if stale.</summary>
    public void OnShown()
    {
        bool added = SyncCards();
        _poller.Start();

        // A team starred on another page has no data yet even if the rest is fresh.
        if (added)
        {
            _ = _poller.RefreshNowAsync();
        }
    }

    /// <summary>The flyout hid or another page covers this one: stop polling.</summary>
    public void OnHidden() => _poller.Stop();

    public void Dispose() => _poller.Stop();

    /// <summary>Matches the cards to the favorites list, keeping loaded data. True if a new team appeared.</summary>
    private bool SyncCards()
    {
        IReadOnlyList<FavoriteTeam> favorites = FavoritesService.Items;
        Dictionary<string, FavoriteCard> existing = _cards.ToDictionary(c => c.Team.Id);
        bool added = false;

        if (!_cards.Select(c => c.Team).SequenceEqual(favorites))
        {
            _cards.Clear();
            foreach (FavoriteTeam team in favorites)
            {
                if (existing.TryGetValue(team.Id, out FavoriteCard? card) && card.Team == team)
                {
                    _cards.Add(card);
                }
                else
                {
                    _cards.Add(new FavoriteCard(team));
                    added = true;
                }
            }
        }

        bool empty = favorites.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        FavoritesList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.IsEnabled = !empty;
        if (empty)
        {
            StatusBar.IsOpen = false;
            UpdatedText.Text = "";
            App.Current.SetTrayStatus(null);
        }

        return added;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_cards.Count == 0)
        {
            return;
        }

        LoadingRing.IsActive = true;
        try
        {
            SportsDbClient client = SportsData.Client;
            Exception? firstError = null;
            int failures = 0;

            // One card at a time, top first, so the most important team fills in first and a
            // rate-limit wait delays the bottom of the list, not all of it.
            foreach (FavoriteCard card in _cards.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Task<Game?> last = client.LastGameAsync(card.Team.Id, ct: cancellationToken);
                    Task<Game?> next = client.NextGameAsync(card.Team.Id, ct: cancellationToken);
                    card.Update(await last, await next);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    failures++;
                    firstError ??= ex;
                }
            }

            UpdateTrayTooltip();

            // Some cards updated: show what we have and let the poller retry the rest on schedule.
            if (failures == _cards.Count && firstError is not null)
            {
                throw firstError;
            }

            StatusBar.IsOpen = false;
            UpdatedText.Text = $"Updated {DateTime.Now:t}";
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void UpdateTrayTooltip()
    {
        // Up to the 127-character tooltip limit; App trims anything over.
        IEnumerable<string> lines = _cards
            .Select(c => GameFormatter.TooltipLine(c.Team.Name, c.Last, c.Team.Id))
            .OfType<string>()
            .Take(3);
        string status = string.Join("\n", lines);
        App.Current.SetTrayStatus(status.Length == 0 ? null : status);
    }

    private void ShowError(Exception ex)
    {
        bool online = ConnectivityService.IsInternetAvailable;
        if (ex is SportsDbRateLimitedException)
        {
            StatusBar.Title = "Too many requests";
            StatusBar.Message = "TheSportsDB's free plan allows 30 a minute. This will update shortly.";
        }
        else
        {
            StatusBar.Title = online ? "Can't reach TheSportsDB" : "No internet connection";
            StatusBar.Message = ex is HttpRequestException or TaskCanceledException
                ? online ? "Retrying shortly." : "This will update when you're back online."
                : ex.Message;
        }

        StatusBar.IsOpen = true;
    }

    private void FavoritesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FavoriteCard card)
        {
            _host.ShowTeam(card.Team);
        }
    }

    private void RecapButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            Links.Open(url);
        }
    }

    private void Card_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: FavoriteCard card })
        {
            return;
        }

        args.Handled = true;
        int index = _cards.IndexOf(card);
        string teamId = card.Team.Id;

        MenuFlyout menu = new();
        MenuFlyoutItem up = new() { Text = "Move up", Icon = new FontIcon { Glyph = "" }, IsEnabled = index > 0 };
        up.Click += (_, _) => MoveCard(teamId, -1);
        MenuFlyoutItem down = new() { Text = "Move down", Icon = new FontIcon { Glyph = "" }, IsEnabled = index < _cards.Count - 1 };
        down.Click += (_, _) => MoveCard(teamId, 1);
        MenuFlyoutItem icon = new() { Text = "Use logo as tray icon", Icon = new FontIcon { Glyph = "" } };
        icon.Click += async (_, _) => await UseAsTrayIconAsync(teamId);
        MenuFlyoutItem remove = new() { Text = "Remove from favorites", Icon = new FontIcon { Glyph = "" } };
        remove.Click += (_, _) =>
        {
            FavoritesService.Remove(teamId);
            TrayIconService.ForgetTeam(teamId);
            SyncCards();
            UpdateTrayTooltip();
        };

        menu.Items.Add(up);
        menu.Items.Add(down);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(icon);
        menu.Items.Add(remove);

        if (args.TryGetPosition(sender, out Windows.Foundation.Point point))
        {
            menu.ShowAt(sender, new FlyoutShowOptions { Position = point });
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
    }

    private void MoveCard(string teamId, int delta)
    {
        FavoritesService.Move(teamId, delta);
        SyncCards();
        UpdateTrayTooltip();
    }

    private async Task UseAsTrayIconAsync(string teamId)
    {
        try
        {
            await TrayIconService.SetChoiceAsync(TrayIconChoice.Team(teamId));
        }
        catch (Exception ex)
        {
            StatusBar.Title = "Can't use this logo";
            StatusBar.Message = ex is HttpRequestException ? "The logo didn't download. Try again when you're online." : ex.Message;
            StatusBar.IsOpen = true;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => _host.ShowFindTeam();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshForcedAsync();

    private async Task RefreshForcedAsync()
    {
        // The refresh button skips the 5-minute schedule cache (but not the 30-second floor).
        LoadingRing.IsActive = true;
        try
        {
            foreach (FavoriteCard card in _cards.ToList())
            {
                Task<Game?> last = SportsData.Client.LastGameAsync(card.Team.Id, force: true);
                Task<Game?> next = SportsData.Client.NextGameAsync(card.Team.Id, force: true);
                card.Update(await last, await next);
            }

            StatusBar.IsOpen = false;
            UpdatedText.Text = $"Updated {DateTime.Now:t}";
            UpdateTrayTooltip();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => _host.ShowSettings();
}
