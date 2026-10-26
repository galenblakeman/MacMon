using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Platform;
using Avalonia.VisualTree;
using WinMon.Monitoring;
using MonCore.MacOS;

namespace MacMon;

public sealed partial class SettingsWindow : Window
{
    private readonly Func<SettingsValues, Task<string?>> _save;
    private bool _saving;
    public int Section => Sections.SelectedIndex;
    public SettingsWindow() : this(new MacPreferences(), [], 0, false, _ => Task.FromResult<string?>(null)) { }
    public SettingsWindow(MacPreferences preferences, IReadOnlyList<MacNetworkChoice> networks, int section,
        bool canSave, Func<SettingsValues, Task<string?>> save)
    {
        _save = save;
        InitializeComponent();
        TopmostSetting.IsChecked = preferences.AlwaysOnTop;
        KeepHistorySetting.IsChecked = preferences.KeepHistory;
        RangeChoice.SelectedIndex = preferences.HistoryRange;
        ThemeChoice.ItemsSource = Appearance.Themes; ThemeChoice.SelectedItem = preferences.Theme;
        PaletteChoice.ItemsSource = Appearance.Palettes; PaletteChoice.SelectedItem = preferences.Palette;
        GaugeChoice.ItemTemplate = new FuncDataTemplate<string>((style, _) => new TextBlock { Text = ResourceGauge.StyleLabel(style) });
        GaugeChoice.ItemsSource = ResourceGauge.GaugeStyles; GaugeChoice.SelectedItem = preferences.GaugeStyle;
        NetworkChoice.ItemsSource = networks;
        NetworkChoice.SelectedItem = networks.FirstOrDefault(network => network.Id == (preferences.NetworkId ?? ""));
        CaptureEditor.Load(preferences.Captures);
        Sections.SelectionChanged += (_, _) => ShowSection();
        ThemeChoice.SelectionChanged += (_, _) => UpdatePreview();
        PaletteChoice.SelectionChanged += (_, _) => UpdatePreview();
        GaugeChoice.SelectionChanged += (_, _) => UpdatePreview();
        ActualThemeVariantChanged += (_, _) => UpdatePreview();
        if (this.GetPlatformSettings() is { } platform)
        {
            platform.ColorValuesChanged += PlatformColorsChanged;
            Closed += (_, _) => platform.ColorValuesChanged -= PlatformColorsChanged;
        }
        Sections.SelectedIndex = Math.Clamp(section, 0, 3);
        ShowSection();
        SaveSettings.IsEnabled = canSave;
        if (!canSave) SettingsStatus.Text = "Settings cannot be saved because the existing file could not be safely loaded. The original file is preserved.";
        CancelSettings.Click += (_, _) => { if (!_saving) Close(); };
        SaveSettings.Click += async (_, _) => await Save();
        Closing += (_, args) => { if (_saving) args.Cancel = true; };
        var now = DateTimeOffset.Now;
        var moment = Stopwatch.GetTimestamp();
        var samples = Enumerable.Range(0, 25).Select(index => new MonitorSample(now.AddSeconds(index - 24),
            24 + 14 * Math.Sin((index - 24) * 0.6), [], 0, false, moment + (index - 24L) * Stopwatch.Frequency)
        {
            Resources = new ResourceSample { TotalMemoryBytes = 100, UsedMemoryBytes = (ulong)(62 + 4 * Math.Sin((index - 24) * 0.3)),
                ReceiveBytesPerSecond = 4.2 * 1048576 - 100000 + Math.Sin((index - 24) * 0.4) * 1000000, SendBytesPerSecond = 100000,
                SecondaryDiskReadBytesPerSecond = 18 * 1048576 - 1000000 + (index % 7 - 3) * 3000000, SecondaryDiskWriteBytesPerSecond = 1000000 }
        }).ToArray();
        PreviewGpu.Update(new(32, 67, 100, "32.0%", "0–100%"), "Device utilization");
        PreviewCpu.Update(new(24, 67, 100, "24.0%", "0–100%"), "Speed unavailable");
        PreviewMemory.Update(new(62, 72, 100, "62.0%", "0–100%"), "9.9 / 16.0 GiB");
        PreviewNetwork.Update(new(4.2 * 1048576, 8 * 1048576, 16 * 1048576, ResourceGaugeHistory.NetworkRate(4.2 * 1048576), "0–" + ResourceGaugeHistory.NetworkRate(16 * 1048576)), "↑ 800.0 Kbps");
        PreviewStorage.Update(new(18 * 1048576, 32 * 1048576, 64 * 1048576, "18 MiB/s", "0–64 MiB/s"), "18 MiB/s combined");
        PreviewChart.RangeSeconds = PreviewActivity.RangeSeconds = 24;
        PreviewChart.Update(samples); PreviewActivity.Update(samples);
        UpdatePreview();
    }

    private void ShowSection()
    {
        GeneralPage.IsVisible = Section == 0; AppearancePage.IsVisible = Section == 1;
        MonitoringPage.IsVisible = Section == 2; CapturesPage.IsVisible = Section == 3;
    }
    private void PlatformColorsChanged(object? sender, PlatformColorValues values) => UpdatePreview();
    private void UpdatePreview()
    {
        if (PreviewScope is null) return;
        var theme = ThemeChoice.SelectedItem as string ?? "Dark";
        var variant = theme == "System" ? this.GetPlatformSettings()?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
            ? ThemeVariant.Dark : ThemeVariant.Light : Appearance.Variant(theme);
        PreviewScope.RequestedThemeVariant = variant;
        var dark = variant == ThemeVariant.Dark;
        var palette = PaletteChoice.SelectedItem as string ?? "Classic";
        Appearance.SetPaletteResources(PreviewScope.Resources, palette, dark);
        PreviewScope.Resources["ResourceGaugeStyle"] = GaugeChoice.SelectedItem as string ?? "Bowl";
        PaletteDescription.Text = palette switch
        {
            "Ocean" => "Cool blues, violet and sea green.",
            "Colorblind" => "Blue, orange, purple and green resource colors.",
            "High contrast" => "Strong resource colors with theme-aware contrast.",
            _ => "MacMon’s teal, purple, blue and gold resource colors."
        };
    }

    private async Task Save()
    {
        if (_saving || !SaveSettings.IsEnabled) return;
        _saving = true; SaveSettings.IsEnabled = CancelSettings.IsEnabled = false;
        SettingsStatus.Text = "Saving settings…";
        try
        {
            var values = new SettingsValues(ThemeChoice.SelectedItem as string ?? "Dark",
                PaletteChoice.SelectedItem as string ?? "Classic", TopmostSetting.IsChecked == true,
                KeepHistorySetting.IsChecked == true, RangeChoice.SelectedIndex,
                (NetworkChoice.SelectedItem as MacNetworkChoice)?.Id, CaptureEditor.Value, GaugeChoice.SelectedItem as string ?? "Bowl");
            var error = await _save(values);
            if (error is not null) { SettingsStatus.Text = error; return; }
            _saving = false;
            Close();
        }
        finally
        {
            _saving = false; SaveSettings.IsEnabled = CancelSettings.IsEnabled = true;
        }
    }
}
