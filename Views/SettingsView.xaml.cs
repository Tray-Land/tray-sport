using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TraySport.Core.Icons;
using TraySport.Core.Models;
using TraySport.Services;
using Windows.ApplicationModel;

namespace TraySport.Views;

/// <summary>A choice in the tray icon picker.</summary>
public sealed class IconOption(TrayIconChoice choice, string label, ImageSource? image)
{
    public TrayIconChoice Choice { get; } = choice;

    public string Label { get; } = label;

    public ImageSource? Image { get; } = image;
}

/// <summary>Settings, as a page in the flyout: the tray icon and start with Windows.</summary>
public sealed partial class SettingsView : UserControl, IFlyoutView
{
    private readonly TrayFlyoutWindow _host;
    private bool _loading = true;
    private CancellationTokenSource? _iconChange;

    internal SettingsView(TrayFlyoutWindow host)
    {
        InitializeComponent();
        _host = host;
        VersionText.Text = $"{App.DisplayName} {GetVersion()}";
        DataPlanText.Text = SportsData.Client.IsFreeTier
            ? "Using TheSportsDB's free data plan (30 requests a minute, limited results)."
            : "Using your TheSportsDB API key.";
        _loading = false;
    }

    public void OnShown()
    {
        // Rebuilt each time: favorites and the taskbar theme may have changed.
        LoadIconOptions();
        _ = LoadStartupStateAsync(); // Windows Settings may have changed it.
        FocusWhenLoaded.Focus(BackButton);
    }

    public void OnHidden() => _iconChange?.Cancel();

    private void LoadIconOptions()
    {
        _loading = true;
        bool light = SystemThemeService.IsLight;
        List<IconOption> options = SportCatalog.Sports
            .OrderBy(s => s.IconKey != TrayIconChoice.DefaultSportKey)
            .Select(s => new IconOption(
                TrayIconChoice.Sport(s.IconKey),
                $"{s.DisplayName} icon",
                new SvgImageSource(new Uri($"ms-appx:///Assets/Sports/{s.IconKey}{(light ? "-dark" : "")}.svg")) { RasterizePixelWidth = 64, RasterizePixelHeight = 64 }))
            .ToList();
        options.AddRange(FavoritesService.Items.Select(f =>
            new IconOption(TrayIconChoice.Team(f.Id), $"{f.Name} logo", Images.Badge(f.TinyBadgeUrl, 32))));

        IconPicker.ItemsSource = options;
        TrayIconChoice current = TrayIconService.Choice;
        IconPicker.SelectedItem = options.FirstOrDefault(o => o.Choice == current) ?? options[0];
        IconHint.Text = FavoritesService.Items.Count == 0
            ? "Sport icons match your taskbar's light or dark theme. Star a team to use its logo."
            : "Sport icons match your taskbar's light or dark theme. Team logos show in full color.";
        _loading = false;
    }

    private async void IconPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || IconPicker.SelectedItem is not IconOption option || option.Choice == TrayIconService.Choice)
        {
            return;
        }

        _iconChange?.Cancel();
        CancellationTokenSource cts = new();
        _iconChange = cts;
        IconError.IsOpen = false;
        try
        {
            await TrayIconService.SetChoiceAsync(option.Choice, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Left the page before the logo downloaded; keep the previous icon.
        }
        catch (Exception ex)
        {
            IconError.Title = "Can't use this logo";
            IconError.Message = ex is HttpRequestException ? "The logo didn't download. Try again when you're online." : ex.Message;
            IconError.IsOpen = true;
            LoadIconOptions(); // Put the selection back on the icon that's actually showing.
        }
    }

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadStartupStateAsync() => ShowStartupState(await StartupService.GetStateAsync());

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say; the app
        // can't override it, so say where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep your scores a click away after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }

    private void DataSourceLink_Click(object sender, RoutedEventArgs e) => Links.Open("https://www.thesportsdb.com");

    private void BackButton_Click(object sender, RoutedEventArgs e) => _host.GoBack();
}
