using TraySport.Core.Models;

namespace TraySport.Services;

/// <summary>The starred teams, in the user's order, persisted in LocalSettings.</summary>
internal static class FavoritesService
{
    private const string Key = "Favorites";

    private static IReadOnlyList<FavoriteTeam>? s_items;

    /// <summary>Raised on the UI thread (every change comes from the UI) after the list changes.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<FavoriteTeam> Items =>
        s_items ??= SettingsService.GetJson(Key, FavoritesJsonContext.Default.ListFavoriteTeam) ?? [];

    public static bool IsFavorite(string teamId) => Items.Any(f => f.Id == teamId);

    public static bool IsFull => Items.Count >= FavoritesPolicy.MaxFavorites;

    public static FavoriteTeam? Find(string teamId) => Items.FirstOrDefault(f => f.Id == teamId);

    /// <summary>Stars the team, or returns false when the list is full.</summary>
    public static bool Add(FavoriteTeam team)
    {
        if (!IsFavorite(team.Id) && IsFull)
        {
            return false;
        }

        Save(FavoritesPolicy.Add(Items, team));
        return true;
    }

    public static void Remove(string teamId) => Save(FavoritesPolicy.Remove(Items, teamId));

    public static void Move(string teamId, int delta) => Save(FavoritesPolicy.Move(Items, teamId, delta));

    private static void Save(IReadOnlyList<FavoriteTeam> items)
    {
        if (ReferenceEquals(items, s_items))
        {
            return;
        }

        s_items = items;
        SettingsService.SetJson(Key, items.ToList(), FavoritesJsonContext.Default.ListFavoriteTeam);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
