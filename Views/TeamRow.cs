using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using TraySport.Core.Models;
using TraySport.Services;

namespace TraySport.Views;

/// <summary>A team in the Find a team list, with its star state.</summary>
public sealed partial class TeamRow(Team team) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Team Team { get; } = team;

    public BitmapImage? Badge { get; } = Images.Badge(team.TinyBadgeUrl, 32);

    public string Subtitle => string.Join(" · ", new[] { Team.League, Team.Country }.Where(s => !string.IsNullOrEmpty(s)));

    public bool IsFavorite => FavoritesService.IsFavorite(Team.Id);

    public string StarGlyph => IsFavorite ? "" : "";

    public string StarLabel => IsFavorite ? $"Remove {Team.Name} from favorites" : $"Add {Team.Name} to favorites";

    /// <summary>Re-reads the star state (it may have changed on another page, or a toggle was refused).</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
