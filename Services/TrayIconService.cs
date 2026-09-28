using System.Runtime.InteropServices.WindowsRuntime;
using TraySport.Core.Icons;
using TraySport.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TraySport.Services;

/// <summary>
/// Resolves the user's tray icon choice to an .ico file. Sport glyphs ship in Assets\Sports in a
/// white and a black ("-dark") variant for the taskbar theme. A team's badge is downloaded once,
/// rendered at each tray size, and kept as an .ico in LocalFolder, so choosing it costs one
/// request and later launches cost none.
/// </summary>
internal static class TrayIconService
{
    // The notification area draws 16 px at 100% up to 32 px at 200%; 64 covers larger scales.
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64];

    /// <summary>Raised on the UI thread after the choice changes and its icon file is ready.</summary>
    public static event EventHandler? Changed;

    public static TrayIconChoice Choice => TrayIconChoice.Parse(SettingsService.TrayIcon);

    /// <summary>
    /// The icon to show right now, without any I/O beyond a file check: the chosen team's cached
    /// badge, or the default glyph if it isn't there yet.
    /// </summary>
    public static string CurrentPath
    {
        get
        {
            TrayIconChoice choice = Choice;
            if (!choice.IsTeam)
            {
                return SportIconPath(choice.Key);
            }

            string teamPath = TeamIconPath(choice.Key);
            return File.Exists(teamPath) ? teamPath : SportIconPath(TrayIconChoice.DefaultSportKey);
        }
    }

    public static string SportIconPath(string iconKey, bool? lightTaskbar = null) => Path.Combine(
        AppContext.BaseDirectory, "Assets", "Sports",
        TrayIconChoice.SportIconFileName(iconKey, lightTaskbar ?? SystemThemeService.IsLight));

    /// <summary>
    /// Switches the tray icon. A team badge is rendered first, so on failure (offline, bad
    /// image) the old choice stays and the exception tells the caller why.
    /// </summary>
    public static async Task SetChoiceAsync(TrayIconChoice choice, CancellationToken cancellationToken = default)
    {
        if (choice.IsTeam)
        {
            await EnsureTeamIconAsync(choice.Key, cancellationToken);
        }

        SettingsService.TrayIcon = choice.ToString();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>At launch: if the chosen team's icon file went missing, rebuild it in the background.</summary>
    public static async Task RepairAsync()
    {
        TrayIconChoice choice = Choice;
        if (!choice.IsTeam || File.Exists(TeamIconPath(choice.Key)))
        {
            return;
        }

        try
        {
            await EnsureTeamIconAsync(choice.Key, CancellationToken.None);
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            // Offline or the badge is gone: keep the default glyph until next launch.
        }
    }

    /// <summary>Deletes a team's rendered icon when it's no longer needed.</summary>
    public static void ForgetTeam(string teamId)
    {
        if (Choice is { IsTeam: true } choice && choice.Key == teamId)
        {
            return;
        }

        try
        {
            File.Delete(TeamIconPath(teamId));
        }
        catch
        {
            // Best effort.
        }
    }

    private static string IconFolder => Path.Combine(ApplicationData.Current.LocalFolder.Path, "TrayIcons");

    private static string TeamIconPath(string teamId) => Path.Combine(IconFolder, $"team-{teamId}.ico");

    private static async Task EnsureTeamIconAsync(string teamId, CancellationToken cancellationToken)
    {
        string path = TeamIconPath(teamId);
        if (File.Exists(path))
        {
            return;
        }

        string? badgeUrl = FavoritesService.Find(teamId)?.BadgeUrl
            ?? (await SportsData.Client.LookupTeamAsync(teamId, cancellationToken))?.BadgeUrl;

        // "small" is ~250 px: plenty to downscale from, and a fraction of the original's size.
        if (ImageUrls.Small(badgeUrl) is not { } url)
        {
            throw new InvalidOperationException("This team has no logo to use.");
        }

        byte[] ico = await RenderIcoAsync(await SportsData.GetBytesAsync(url, cancellationToken));
        Directory.CreateDirectory(IconFolder);
        string temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, ico, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    private static async Task<byte[]> RenderIcoAsync(byte[] image)
    {
        using InMemoryRandomAccessStream input = new();
        await input.WriteAsync(image.AsBuffer());
        input.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input);

        List<(int Size, byte[] Png)> images = [];
        foreach (int size in IconSizes)
        {
            (int width, int height) = IconPixels.FitInSquare((int)decoder.PixelWidth, (int)decoder.PixelHeight, size);
            BitmapTransform transform = new()
            {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            PixelDataProvider data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            byte[] square = IconPixels.CenterInSquare(data.DetachPixelData(), width, height, size);

            using InMemoryRandomAccessStream output = new();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)size, (uint)size, 96, 96, square);
            await encoder.FlushAsync();

            byte[] png = new byte[output.Size];
            output.Seek(0);
            await output.ReadAsync(png.AsBuffer(), (uint)png.Length, InputStreamOptions.None);
            images.Add((size, png));
        }

        return IcoWriter.Write(images);
    }
}
