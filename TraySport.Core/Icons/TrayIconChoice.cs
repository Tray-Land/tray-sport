using TraySport.Core.Models;

namespace TraySport.Core.Icons;

/// <summary>
/// What the tray icon shows: a sport glyph (recolored for the taskbar theme) or a favorite team's
/// badge (full color). Stored as "sport:basketball" or "team:134867".
/// </summary>
public readonly record struct TrayIconChoice(bool IsTeam, string Key)
{
    public const string DefaultSportKey = "soccer";

    public static TrayIconChoice Default => Sport(DefaultSportKey);

    public static TrayIconChoice Sport(string iconKey) => new(false, iconKey);

    public static TrayIconChoice Team(string teamId) => new(true, teamId);

    public static TrayIconChoice Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        int colon = value.IndexOf(':');
        if (colon <= 0 || colon == value.Length - 1)
        {
            return Default;
        }

        string kind = value[..colon];
        string key = value[(colon + 1)..].Trim();
        return kind switch
        {
            "team" when key.All(char.IsAsciiDigit) => Team(key),
            "sport" when SportCatalog.FindByIconKey(key) is not null => Sport(key.ToLowerInvariant()),
            _ => Default,
        };
    }

    public override string ToString() => (IsTeam ? "team:" : "sport:") + Key;

    /// <summary>The packaged icon file for a sport glyph; the "-dark" variant is for light taskbars.</summary>
    public static string SportIconFileName(string iconKey, bool lightTaskbar) =>
        $"{iconKey}{(lightTaskbar ? "-dark" : "")}.ico";
}
