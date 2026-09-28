using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TraySport.Core.Formatting;
using TraySport.Core.Models;

namespace TraySport.Views;

/// <summary>One starred team's card on the favorites page: its last result and next game.</summary>
public sealed partial class FavoriteCard(FavoriteTeam team) : INotifyPropertyChanged
{
    private Game? _last;
    private Game? _next;
    private bool _loaded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public FavoriteTeam Team { get; } = team;

    public string Name => Team.Name;

    public string League => Team.League ?? Team.Sport;

    public BitmapImage? Badge { get; } = Images.Badge(team.TinyBadgeUrl, 40);

    public Game? Last => _last;

    public Game? Next => _next;

    public Visibility LastVisibility => Show(_last is not null);

    public Visibility NextVisibility => Show(_next is not null);

    public Visibility EmptyVisibility => Show(_loaded && _last is null && _next is null);

    public Visibility WinVisibility => Show(Outcome == GameOutcome.Win);

    public Visibility LossVisibility => Show(Outcome == GameOutcome.Loss);

    public Visibility DrawVisibility => Show(Outcome == GameOutcome.Draw);

    public Visibility RecapVisibility => Show(_last?.HasScore == true);

    public string LastScore => _last is null ? "" : GameFormatter.Score(_last, Team.Id);

    public string LastMatchup => _last is null ? "" : GameFormatter.Matchup(_last, Team.Id);

    public string LastWhen => _last is null ? "" : When(_last);

    public string NextMatchup => _next is null ? "" : GameFormatter.Matchup(_next, Team.Id);

    public string NextWhen => _next is null ? "" : When(_next);

    public string RecapUrl => _last is null ? "" : GameFormatter.RecapUrl(_last);

    public string RecapLabel => $"Watch {Team.Name} recap on YouTube";

    public string AccessibleSummary
    {
        get
        {
            string summary = Team.Name;
            if (_last is not null)
            {
                summary += $", last game {GameFormatter.OutcomeLetter(Outcome)} {LastScore} {LastMatchup}, {LastWhen}";
            }

            if (_next is not null)
            {
                summary += $", next game {NextMatchup}, {NextWhen}";
            }

            return summary;
        }
    }

    private GameOutcome Outcome => _last is null ? GameOutcome.Unknown : GameFormatter.Outcome(_last, Team.Id);

    public void Update(Game? last, Game? next)
    {
        _last = last;
        _next = next;
        _loaded = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // every derived property
    }

    private static string When(Game game) => GameFormatter.When(
        game.StartsAt, game.HasStartTime, DateTimeOffset.Now, TimeZoneInfo.Local, CultureInfo.CurrentCulture);

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
