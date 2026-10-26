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
        bool canSave, Func<SettingsValues, Task<string?>> save, IReadOnlyList<MacDiskChoice>? disks = null)
    {
        preferences = preferences.Normalized(preferences.BootId);
        _save = save;
        InitializeComponent();
        LanguageChoice.ItemsSource = UiText.Languages;
        // Avalonia passes null while clearing/recycling dropdown containers.
        LanguageChoice.ItemTemplate = new FuncDataTemplate<LanguageChoice>((choice, _) => choice is null ? null : UiText.Label(choice.Label));
        LanguageChoice.SelectedItem = UiText.Languages.First(c => c.Id == preferences.Language);
        StartupViewChoice.ItemsSource = new[] { "Last view", "Full view", "Compact view" };
        StartupViewChoice.ItemTemplate = new FuncDataTemplate<string>((value, _) => value is null ? null : UiText.Label(value));
        StartupViewChoice.SelectedIndex = preferences.StartupView switch { "Full" => 1, "Compact" => 2, _ => 0 };
        ShortcutChoice.ItemsSource = MacDesktopIntegration.Shortcuts;
        ShortcutChoice.ItemTemplate = new FuncDataTemplate<ShortcutChoice>((choice, _) => choice is null ? null : UiText.Label(choice.Label));
        ShortcutChoice.SelectedItem = MacDesktopIntegration.Shortcuts.First(c => c.Id == preferences.ShowShortcut);
        DefaultShortcut.Click += (_, _) => ShortcutChoice.SelectedItem = MacDesktopIntegration.Shortcuts[1];
        var loginStatus = MacDesktopIntegration.LoginStatus;
        OpenAtLogin.IsEnabled = loginStatus >= 0;
        OpenAtLogin.IsChecked = loginStatus is 1 or 2;
        StartupStatus.Text = UiText.Get(loginStatus switch { 1 => "Opens at login.", 2 => "Approval required in Login Items.",
            >= 0 => "Off", _ => "Available in the packaged MacMon app." });
        LoginItems.IsEnabled = loginStatus >= 0;
        LoginItems.Click += (_, _) => MacDesktopIntegration.OpenLoginSettings();
        TopmostSetting.IsChecked = preferences.AlwaysOnTop;
        KeepHistorySetting.IsChecked = preferences.KeepHistory;
        RangeChoice.SelectedIndex = preferences.HistoryRange;
        ThemeChoice.ItemTemplate = new FuncDataTemplate<string>((theme, _) => theme is null ? null : UiText.Label(Appearance.ThemeLabel(theme)));
        ThemeChoice.ItemsSource = Appearance.Themes; ThemeChoice.SelectedItem = preferences.Theme;
        PaletteChoice.ItemsSource = Appearance.Palettes; PaletteChoice.SelectedItem = preferences.Palette;
        GaugeChoice.ItemTemplate = new FuncDataTemplate<string>((style, _) => style is null ? null : UiText.Label(ResourceGauge.StyleLabel(style)));
        GaugeChoice.ItemsSource = ResourceGauge.GaugeStyles; GaugeChoice.SelectedItem = preferences.GaugeStyle;
        NetworkChoice.ItemTemplate = new FuncDataTemplate<MacNetworkChoice>((choice, _) => choice is null ? null : UiText.Label(choice.Name));
        PrimaryDiskChoice.ItemTemplate = SecondaryDiskChoice.ItemTemplate = new FuncDataTemplate<MacDiskChoice>((choice, _) => choice is null ? null : UiText.Label(choice.Name));
        NetworkChoice.ItemsSource = networks;
        NetworkChoice.SelectedItem = networks.FirstOrDefault(network => network.Id == (preferences.NetworkId ?? ""));
        var availableDisks = disks ?? [];
        MacDiskChoice[] Choices(string? selected, string fallback) => [new("", fallback), .. availableDisks,
            .. selected is { Length: > 0 } && availableDisks.All(d => d.Id != selected) ? new[] { new MacDiskChoice(selected, selected + " · disconnected") } : []];
        PrimaryDiskChoice.ItemsSource = Choices(preferences.PrimaryDiskId, "All block-storage drivers");
        SecondaryDiskChoice.ItemsSource = Choices(preferences.SecondaryDiskId, "Off");
        PrimaryDiskChoice.SelectedItem = PrimaryDiskChoice.Items.OfType<MacDiskChoice>().First(d => d.Id == (preferences.PrimaryDiskId ?? ""));
        SecondaryDiskChoice.SelectedItem = SecondaryDiskChoice.Items.OfType<MacDiskChoice>().First(d => d.Id == (preferences.SecondaryDiskId ?? ""));
        CaptureEditor.Load(preferences.Captures);
        Sections.SelectionChanged += (_, _) => ShowSection();
        var previousTheme = preferences.Theme;
        ThemeChoice.SelectionChanged += (_, _) =>
        {
            var theme = ThemeChoice.SelectedItem as string ?? "Dark";
            if (PaletteChoice.SelectedItem as string == Appearance.DefaultPalette(previousTheme)) PaletteChoice.SelectedItem = Appearance.DefaultPalette(theme);
            previousTheme = theme; UpdatePreview();
        };
        DefaultPalette.Click += (_, _) => PaletteChoice.SelectedItem = Appearance.DefaultPalette(ThemeChoice.SelectedItem as string);
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
        if (!canSave) SettingsStatus.Text = UiText.Get("Settings cannot be saved because the existing file could not be safely loaded. The original file is preserved.");
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
        PreviewGpu.Update(new(32, 67, 100, "32.0%", "0–100%"), UiText.Get("Device utilization"));
        PreviewCpu.Update(new(24, 67, 100, "24.0%", "0–100%"), UiText.Get("Speed unavailable"));
        PreviewMemory.Update(new(62, 72, 100, "62.0%", "0–100%"), "9.9 / 16.0 GiB");
        PreviewNetwork.Update(new(4.2 * 1048576, 8 * 1048576, 16 * 1048576, ResourceGaugeHistory.NetworkRate(4.2 * 1048576), "0–" + ResourceGaugeHistory.NetworkRate(16 * 1048576)), "↑ 800.0 Kbps");
        PreviewStorage.Update(new(18, 32, 100, "18.0%", "0–100%"), "18 MiB/s");
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
        Appearance.SetThemeResources(PreviewScope.Resources, theme, dark);
        Appearance.SetPaletteResources(PreviewScope.Resources, palette, dark, theme);
        PaletteChoice.ItemTemplate = new FuncDataTemplate<string>((choice, _) => choice is null ? null : PalettePreview(choice, theme, dark));
        PreviewScope.Resources["ResourceGaugeStyle"] = GaugeChoice.SelectedItem as string ?? "Bowl";
    }
    private static Control PalettePreview(string palette, string theme, bool dark)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
        var label = UiText.Label(palette); label.Width = 145; label.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(label, UiText.Get(palette));
        label.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; row.Children.Add(label);
        var colors = Appearance.Colors(palette, dark, true, theme).All;
        var swatches = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 3 };
        foreach (var i in new[] { 0, 1, 3, 2, 4, 5 })
        {
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new(2), Background = new Avalonia.Media.SolidColorBrush(colors[i]) };
            ToolTip.SetTip(swatch, ResourceCharts.Names[i]); swatches.Children.Add(swatch);
        }
        row.Children.Add(swatches); return row;
    }

    private async Task Save()
    {
        if (_saving || !SaveSettings.IsEnabled) return;
        var primaryId = (PrimaryDiskChoice.SelectedItem as MacDiskChoice)?.Id;
        var secondaryId = (SecondaryDiskChoice.SelectedItem as MacDiskChoice)?.Id;
        if (!string.IsNullOrEmpty(primaryId) && primaryId == secondaryId)
        { SettingsStatus.Text = UiText.Get("Choose a different second disk, or turn it off."); return; }
        _saving = true; SaveSettings.IsEnabled = CancelSettings.IsEnabled = false;
        SettingsStatus.Text = UiText.Get("Saving settings…");
        try
        {
            var values = new SettingsValues(ThemeChoice.SelectedItem as string ?? "Dark",
                PaletteChoice.SelectedItem as string ?? "Classic", TopmostSetting.IsChecked == true,
                KeepHistorySetting.IsChecked == true, RangeChoice.SelectedIndex,
                (NetworkChoice.SelectedItem as MacNetworkChoice)?.Id, CaptureEditor.Value, GaugeChoice.SelectedItem as string ?? "Bowl",
                string.IsNullOrEmpty(primaryId) ? null : primaryId, string.IsNullOrEmpty(secondaryId) ? null : secondaryId,
                (LanguageChoice.SelectedItem as LanguageChoice)?.Id ?? "System",
                StartupViewChoice.SelectedIndex switch { 1 => "Full", 2 => "Compact", _ => "Last" },
                (ShortcutChoice.SelectedItem as ShortcutChoice)?.Id ?? "Off", OpenAtLogin.IsEnabled ? OpenAtLogin.IsChecked == true : null);
            var error = await _save(values);
            if (error is not null) { SettingsStatus.Text = UiText.Get(error); return; }
            _saving = false;
            Close();
        }
        finally
        {
            _saving = false; SaveSettings.IsEnabled = CancelSettings.IsEnabled = true;
        }
    }
}
