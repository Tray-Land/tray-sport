using TraySport.Core.Models;

namespace TraySport.Services;

/// <summary>
/// Loads the bundled team catalog (Assets\teams.json) on first use and drops it when the app goes
/// idle, so it only costs memory while someone is looking for a team.
/// </summary>
internal static class TeamCatalogService
{
    private static TeamCatalog? s_catalog;

    public static TeamCatalog Catalog => s_catalog ??= Load();

    public static void Unload() => s_catalog = null;

    private static TeamCatalog Load()
    {
        try
        {
            return TeamCatalog.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "teams.json")));
        }
        catch (IOException)
        {
            return TeamCatalog.Empty;
        }
    }
}
