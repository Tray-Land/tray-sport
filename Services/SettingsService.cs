using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Windows.Storage;

namespace TraySport.Services;

/// <summary>
/// Typed wrapper over <see cref="ApplicationData.LocalSettings"/>. Every read falls back to a
/// default so a missing or corrupt value never crashes the app. Not for secrets: see
/// <see cref="Secrets"/> (build-time app keys) and <see cref="CredentialStore"/> (user tokens).
/// </summary>
/// <remarks>
/// LocalSettings values must be small (8 KB each) WinRT primitives. Store enums as int, and lists
/// or records as JSON through a source-generated <c>JsonSerializerContext</c> (trim-safe) with
/// <see cref="GetJson"/> / <see cref="SetJson"/>. Anything larger belongs in a file under
/// <c>ApplicationData.Current.LocalFolder</c>.
/// </remarks>
internal static class SettingsService
{
    /// <summary>False until the first launch has shown the flyout.</summary>
    public static bool HasLaunchedBefore
    {
        get => Get(nameof(HasLaunchedBefore), false);
        set => Set(nameof(HasLaunchedBefore), value);
    }

    /// <summary>The tray icon choice, as <c>TrayIconChoice.ToString()</c> ("sport:soccer", "team:133604").</summary>
    public static string? TrayIcon
    {
        get => Get<string?>(nameof(TrayIcon), null);
        set => Set(nameof(TrayIcon), value);
    }

    /// <summary>The sport last browsed in Find a team, by icon key.</summary>
    public static string? LastSport
    {
        get => Get<string?>(nameof(LastSport), null);
        set => Set(nameof(LastSport), value);
    }

    /// <summary>The league last browsed in Find a team.</summary>
    public static string? LastLeagueId
    {
        get => Get<string?>(nameof(LastLeagueId), null);
        set => Set(nameof(LastLeagueId), value);
    }

    public static T? GetJson<T>(string key, JsonTypeInfo<T> typeInfo)
    {
        if (Get<string?>(key, null) is not { } json)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static void SetJson<T>(string key, T? value, JsonTypeInfo<T> typeInfo) =>
        Set(key, value is null ? null : JsonSerializer.Serialize(value, typeInfo));

    private static T Get<T>(string key, T defaultValue)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value) && value is T typed
                ? typed
                : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void Set(string key, object? value)
    {
        try
        {
            if (value is null)
            {
                ApplicationData.Current.LocalSettings.Values.Remove(key);
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
        }
        catch
        {
            // Settings are best-effort; ignore storage failures.
        }
    }
}
