using System.Collections.ObjectModel;
using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

    // Cards that get their first data together reveal this far apart, top to bottom.
    private static readonly TimeSpan RevealStagger = TimeSpan.FromMilliseconds(70);

    // A manual refresh flips the cards over this far apart, top to bottom.
    private static readonly TimeSpan FlipStagger = TimeSpan.FromMilliseconds(90);

    private readonly TrayFlyoutWindow _host;
    private readonly ForegroundPoller _poller;
    private readonly ObservableCollection<FavoriteCard> _cards = [];
    private bool _forceNextRefresh;
    private DateTime _nextRevealAtUtc = DateTime.MinValue;

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
                    _cards.Add(new FavoriteCard(team) { Stagger = _cards.Count });
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

        // The refresh button skips the 5-minute schedule cache (but not the 30-second floor).
        bool force = _forceNextRefresh;
        _forceNextRefresh = false;

        List<FavoriteCard> cards = [.. _cards];
        SetLoading(true);
        foreach (FavoriteCard card in cards)
        {
            card.SetWaiting(true); // Only cards with no data yet show the skeleton.
        }

        try
        {
            SportsDbClient client = SportsData.Client;
            Exception? firstError = null;
            int failures = 0;

            // One card at a time, top first, so the most important team fills in first and a
            // rate-limit wait delays the bottom of the list, not all of it.
            foreach (FavoriteCard card in cards)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Task<Game?> last = client.LastGameAsync(card.Team.Id, force, cancellationToken);
                    Task<Game?> next = client.NextGameAsync(card.Team.Id, force, cancellationToken);
                    Game? lastGame = await last;
                    Game? nextGame = await next;
                    if (!card.IsLoaded)
                    {
                        await WaitForRevealTurnAsync(cancellationToken);
                    }

                    card.Update(lastGame, nextGame);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    failures++;
                    firstError ??= ex;
                }
            }

            UpdateTrayTooltip();

            if (failures == _cards.Count && firstError is not null)
            {
                throw firstError;
            }

            // Some cards updated: show what we have, say why the rest are blank or old, and let
            // the poller retry them on schedule.
            if (firstError is not null)
            {
                ShowError(firstError, partial: true);
            }
            else
            {
                StatusBar.IsOpen = false;
            }

            UpdatedText.Text = $"Updated {DateTime.Now:t}";
        }
        finally
        {
            foreach (FavoriteCard card in cards)
            {
                card.SetWaiting(false);
            }

            SetLoading(false);
        }
    }

    /// <summary>
    /// Spaces out first-time reveals: cards whose data comes from the cache all at once land one
    /// after another, top to bottom, while cards that trickle in from the network land right away.
    /// </summary>
    private async Task WaitForRevealTurnAsync(CancellationToken cancellationToken)
    {
        if (!Motion.IsEnabled)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        TimeSpan wait = _nextRevealAtUtc - now;
        _nextRevealAtUtc = (wait > TimeSpan.Zero ? _nextRevealAtUtc : now) + RevealStagger;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }
    }

    /// <summary>A manual refresh flips each card over in turn, top to bottom, so it's obvious it's happening.</summary>
    private void FlipCards()
    {
        int visible = 0;
        foreach (FavoriteCard card in _cards)
        {
            // Only realized (on-screen or near it) cards have a container to flip.
            if (FavoritesList.ContainerFromItem(card) is ListViewItem { ContentTemplateRoot: UIElement root })
            {
                Motion.Flip(root, FlipStagger * visible++);
            }
        }
    }

    private void SetLoading(bool loading)
    {
        if (loading)
        {
            Motion.StartSpin(RefreshIcon);
        }
        else
        {
            Motion.StopSpin(RefreshIcon);
        }

        AutomationProperties.SetName(RefreshButton, loading ? "Refreshing" : "Refresh");
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

    private void ShowError(Exception ex, bool partial = false)
    {
        bool online = ConnectivityService.IsInternetAvailable;
        if (partial && ex is not SportsDbRateLimitedException)
        {
            StatusBar.Title = "Some teams didn't update";
            StatusBar.Message = "Retrying shortly.";
        }
        else if (ex is SportsDbRateLimitedException)
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // Through the poller, so it's cancelled on hide and never overlaps a scheduled refresh. If
        // one is already running, that refresh is the answer and the flag is dropped unused.
        FlipCards();
        _forceNextRefresh = true;
        await _poller.RefreshNowAsync();
        _forceNextRefresh = false;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => _host.ShowSettings();
}
