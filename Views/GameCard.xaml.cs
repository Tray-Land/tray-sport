using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TraySport.Core.Formatting;
using TraySport.Core.Models;

namespace TraySport.Views;

/// <summary>One game: both teams with badges and scores, when, where, and a recap link once played.</summary>
public sealed partial class GameCard : UserControl
{
    private string? _recapUrl;

    public GameCard()
    {
        InitializeComponent();
    }

    public void Show(Game game, string teamId)
    {
        ContextText.Text = string.Join(" · ", new[] { game.League, game.IsPostponed ? "Postponed" : null }.Where(s => s is not null));
        WhenText.Text = GameFormatter.When(game.StartsAt, game.HasStartTime, DateTimeOffset.Now, TimeZoneInfo.Local, CultureInfo.CurrentCulture);

        HomeName.Text = game.HomeTeam;
        AwayName.Text = game.AwayTeam;
        HomeBadge.Source = Images.Badge(Core.Models.ImageUrls.Tiny(game.HomeBadgeUrl), 24);
        AwayBadge.Source = Images.Badge(Core.Models.ImageUrls.Tiny(game.AwayBadgeUrl), 24);
        HomeScore.Text = game.HomeScore?.ToString(CultureInfo.CurrentCulture) ?? "";
        AwayScore.Text = game.AwayScore?.ToString(CultureInfo.CurrentCulture) ?? "";

        // The favorite's name stands out; so does the winner's score.
        HomeName.FontWeight = game.HomeTeamId == teamId ? FontWeights.SemiBold : FontWeights.Normal;
        AwayName.FontWeight = game.AwayTeamId == teamId ? FontWeights.SemiBold : FontWeights.Normal;
        bool homeWon = game.HasScore && game.HomeScore > game.AwayScore;
        bool awayWon = game.HasScore && game.AwayScore > game.HomeScore;
        HomeScore.Opacity = awayWon ? 0.6 : 1;
        AwayScore.Opacity = homeWon ? 0.6 : 1;

        VenueText.Text = game.Venue ?? "";
        VenueText.Visibility = game.Venue is null ? Visibility.Collapsed : Visibility.Visible;

        _recapUrl = game.HasScore ? GameFormatter.RecapUrl(game) : null;
        RecapButton.Visibility = _recapUrl is null ? Visibility.Collapsed : Visibility.Visible;
        RecapText.Text = game.VideoUrl is null ? "Find the recap on YouTube" : "Watch the recap on YouTube";
        AutomationProperties.SetName(RecapButton, $"{RecapText.Text}: {GameFormatter.Title(game)}");

        string score = game.HasScore ? $", {game.HomeScore} to {game.AwayScore}" : "";
        AutomationProperties.SetName(this, $"{GameFormatter.Title(game)}{score}, {WhenText.Text}");
    }

    private void RecapButton_Click(object sender, RoutedEventArgs e) => Links.Open(_recapUrl);
}
