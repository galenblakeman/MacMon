using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;

[assembly: AvaloniaTestApplication(typeof(MonCore.MacOS.Tests.AppearanceTestAppBuilder))]

namespace MonCore.MacOS.Tests;

public static class AppearanceTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class SettingsWindowTests
{
    [AvaloniaTest]
    public void SpanishSectionsRenderAtMinimumSizeWithLocalizedActions()
    {
        UiText.Apply("es");
        var directory = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (directory is not null) Directory.CreateDirectory(directory);
        var window = new SettingsWindow(new MacPreferences { Language = "es" }, [new("", "Automatic")], 0, true,
            _ => Task.FromResult<string?>(null)) { Width = 800, Height = 560 };
        try
        {
            window.Show();
            foreach (var section in new[] { 0, 1, 2, 3 })
            {
                Get<ListBox>(window, "Sections").SelectedIndex = section;
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                Assert.That(Get<Button>(window, "SaveSettings").Content, Is.EqualTo("Guardar"));
                if (section == 3)
                {
                    foreach (var input in Get<CaptureSettingsEditor>(window, "CaptureEditor").GetVisualDescendants().OfType<NumericUpDown>())
                    {
                        var text = input.GetVisualDescendants().OfType<TextBox>().First();
                        Assert.That(text.Bounds.Width, Is.GreaterThanOrEqualTo(40), input.Name);
                    }
                }
                if (directory is not null) frame!.Save(Path.Combine(directory, $"settings-es-{section}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); UiText.Apply("en"); }
    }

    [AvaloniaTest]
    public void LanguageDraftWaitsForSaveAndExistingLabelsRefreshWithoutChangingIds()
    {
        UiText.Apply("en");
        SettingsValues? saved = null;
        var label = UiText.Label("Settings");
        var window = Window(values => { saved = values; return Task.FromResult<string?>(null); }, section: 0);
        try
        {
            window.Show();
            Get<ComboBox>(window, "LanguageChoice").SelectedItem = UiText.Languages.First(c => c.Id == "es");
            Assert.That(UiText.Language, Is.EqualTo("en"));
            Click(window, "SaveSettings");
            Assert.That(saved!.Language, Is.EqualTo("es"));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            UiText.Apply(saved.Language);
            Dispatcher.UIThread.RunJobs();
            Assert.That(label.Text, Is.EqualTo("Configuración"));
            Assert.That(UiText.Get("Save"), Is.EqualTo("Guardar"));
            Assert.That(UiText.NormalizeLanguage("unknown"), Is.EqualTo("System"));
            Assert.That(Appearance.Themes, Does.Contain("Copper"));
        }
        finally { window.Close(); UiText.Apply("en"); }
    }

    [AvaloniaTest]
    public void ThemeChangesAlwaysSelectTheirDefaultPalette()
    {
        var window = Window();
        Get<ComboBox>(window, "ThemeChoice").SelectedItem = "Copper";
        Assert.That(Get<ComboBox>(window, "PaletteChoice").SelectedItem, Is.EqualTo("Copper"));
        Get<ComboBox>(window, "PaletteChoice").SelectedItem = "Ocean";
        Get<ComboBox>(window, "ThemeChoice").SelectedItem = "Candy";
        Assert.That(Get<ComboBox>(window, "PaletteChoice").SelectedItem, Is.EqualTo("Candy"));
        Get<ComboBox>(window, "PaletteChoice").SelectedItem = "Ocean";
        Click(window, "DefaultPalette");
        Assert.That(Get<ComboBox>(window, "PaletteChoice").SelectedItem, Is.EqualTo("Candy"));
        Get<ComboBox>(window, "ThemeChoice").SelectedItem = "Crystal";
        Assert.That(Get<ComboBox>(window, "PaletteChoice").SelectedItem, Is.EqualTo("Cool Tech"));
        window.Close();
    }
    [AvaloniaTest]
    public void AppearanceCanChangeRepeatedlyWhilePreviewIsVisible()
    {
        var window = Window();
        try
        {
            window.Show();
            foreach (var theme in Appearance.Themes)
            {
                Get<ComboBox>(window, "ThemeChoice").IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                Get<ComboBox>(window, "ThemeChoice").SelectedItem = theme;
                Dispatcher.UIThread.RunJobs();
                Get<ComboBox>(window, "ThemeChoice").IsDropDownOpen = false;
                foreach (var style in ResourceGauge.GaugeStyles)
                {
                    Get<ComboBox>(window, "GaugeChoice").IsDropDownOpen = true;
                    Dispatcher.UIThread.RunJobs();
                    Get<ComboBox>(window, "GaugeChoice").SelectedItem = style;
                    Get<ComboBox>(window, "GaugeChoice").IsDropDownOpen = false;
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.That(frame, Is.Not.Null, $"{theme}/{style}");
                }
                foreach (var palette in Appearance.Palettes)
                {
                    Get<ComboBox>(window, "PaletteChoice").IsDropDownOpen = true;
                    Dispatcher.UIThread.RunJobs();
                    Get<ComboBox>(window, "PaletteChoice").SelectedItem = palette;
                    Get<ComboBox>(window, "PaletteChoice").IsDropDownOpen = false;
                    Dispatcher.UIThread.RunJobs();
                }
                Click(window, "DefaultPalette");
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Close(); }
    }

    private static SettingsWindow Window(Func<SettingsValues, Task<string?>>? save = null, int section = 1, bool writable = true) =>
        new(new MacPreferences(), [new MacNetworkChoice("", "Automatic"), new MacNetworkChoice("en1", "Wi-Fi")],
            section, writable, save ?? (_ => Task.FromResult<string?>(null)));

    private static T Get<T>(Control control, string name) where T : Control => control.FindControl<T>(name)!;
    private static void Click(Control window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaTest]
    public void PreviewAndCancelDoNotMutateApplicationThemePaletteOrSavedValues()
    {
        var app = (App)Application.Current!;
        app.ApplyAppearance("Dark", "Classic");
        var cpu = ((ISolidColorBrush)app.Resources["CpuStroke"]!).Color;
        var saved = false;
        var window = Window(_ => { saved = true; return Task.FromResult<string?>(null); });
        window.Show();
        Get<ComboBox>(window, "ThemeChoice").SelectedItem = "Light";
        Get<ComboBox>(window, "PaletteChoice").SelectedItem = "Ocean";
        Dispatcher.UIThread.RunJobs();
        var chart = Get<CpuChart>(window, "PreviewChart");
        Assert.That(((ISolidColorBrush)chart.CpuBrush).Color, Is.EqualTo(Appearance.Colors("Ocean", false).Cpu));
        Assert.That(app.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Dark));
        Assert.That(((ISolidColorBrush)app.Resources["CpuStroke"]!).Color, Is.EqualTo(cpu));
        Click(window, "CancelSettings");
        Assert.That(saved, Is.False);
        Assert.That(window.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public void SaveCollectsEverySectionAndFailureKeepsTheDraftOpenForRetry()
    {
        SettingsValues? captured = null;
        var fail = true;
        var window = Window(values => { captured = values; return Task.FromResult<string?>(fail ? "Disk full" : null); });
        window.Show();
        Get<CheckBox>(window, "TopmostSetting").IsChecked = true;
        Get<CheckBox>(window, "KeepHistorySetting").IsChecked = true;
        Get<ComboBox>(window, "ThemeChoice").SelectedItem = "Light";
        Get<ComboBox>(window, "PaletteChoice").SelectedItem = "Colorblind";
        Get<ComboBox>(window, "RangeChoice").SelectedIndex = 2;
        Get<ComboBox>(window, "NetworkChoice").SelectedIndex = 1;
        var editor = Get<CaptureSettingsEditor>(window, "CaptureEditor");
        Get<CheckBox>(editor, "EnabledCapture").IsChecked = true;
        Click(window, "SaveSettings");
        Assert.That(window.IsVisible, Is.True);
        Assert.That(Get<TextBlock>(window, "SettingsStatus").Text, Is.EqualTo("Disk full"));
        Assert.That(captured!.AlwaysOnTop && captured.KeepHistory && captured.Captures.Enabled, Is.True);
        Assert.That(captured.Theme, Is.EqualTo("Light"));
        Assert.That(captured.Palette, Is.EqualTo("Colorblind"));
        Assert.That(captured.HistoryRange, Is.EqualTo(2));
        Assert.That(captured.NetworkId, Is.EqualTo("en1"));
        fail = false;
        Click(window, "SaveSettings");
        Assert.That(window.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public async Task PendingSaveCannotCloseOrSubmitTwiceAndProtectedSettingsCannotSave()
    {
        var pending = new TaskCompletionSource<string?>();
        var calls = 0;
        var window = Window(_ => { calls++; return pending.Task; });
        window.Show();
        Click(window, "SaveSettings");
        Click(window, "SaveSettings");
        window.Close();
        Assert.That(window.IsVisible, Is.True);
        Assert.That(calls, Is.EqualTo(1));
        pending.SetResult("Try again");
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.That(Get<Button>(window, "SaveSettings").IsEnabled, Is.True);
        Click(window, "CancelSettings");
        var readOnly = Window(_ => { calls++; return Task.FromResult<string?>(null); }, writable: false);
        readOnly.Show();
        Click(readOnly, "SaveSettings");
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(Get<TextBlock>(readOnly, "SettingsStatus").Text, Does.Contain("preserved"));
        readOnly.Close();
    }

    [AvaloniaTest]
    public void GlobalThemeAndPaletteUpdateAttachedChartAndSparklineResources()
    {
        var app = (App)Application.Current!;
        var chart = new CpuChart();
        var sparkline = new ProcessSparkline();
        var window = new Window { Content = new StackPanel { Children = { chart, sparkline } } };
        window.Show();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            app.ApplyAppearance(theme, "Ocean");
            Dispatcher.UIThread.RunJobs();
            Assert.That(((ISolidColorBrush)chart.CpuBrush).Color, Is.EqualTo(Appearance.Colors("Ocean", theme == "Dark").Cpu));
            Assert.That(sparkline.Stroke, Is.EqualTo(chart.CpuBrush));
            Assert.That(((ISolidColorBrush)window.Background!).Color, Is.EqualTo(Color.Parse(theme == "Dark" ? "#0E1721" : "#EDF2F7")));
        }
        window.Close();
    }

    [AvaloniaTest]
    public void SelectedMetricRowsUseThemeForegroundForLegibility()
    {
        var app = (App)Application.Current!;
        foreach (var theme in new[] { "Light", "Dark" })
        {
            app.ApplyAppearance(theme, "Classic");
            var text = new TextBlock { Text = "12.5%", Classes = { "cpuMetric" } };
            var item = new ListBoxItem { Content = text };
            var list = new ListBox { ItemsSource = new[] { item }, SelectedIndex = 0 };
            var window = new Window { Content = list };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(((ISolidColorBrush)text.Foreground!).Color, Is.EqualTo(Color.Parse(theme == "Dark" ? "#F2F5F8" : "#17222E")));
            list.SelectedIndex = -1;
            Dispatcher.UIThread.RunJobs();
            Assert.That(((ISolidColorBrush)text.Foreground!).Color, Is.EqualTo(Appearance.Colors("Classic", theme == "Dark", true).Cpu));
            window.Close();
        }
    }

    [AvaloniaTest]
    public void AllSectionsAndAppearancePalettesRenderAtMinimumSize()
    {
        var app = (App)Application.Current!;
        var directory = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (directory is not null) Directory.CreateDirectory(directory);
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var palette in Appearance.Palettes)
        {
            app.ApplyAppearance(theme, palette);
            var preferences = new MacPreferences { Theme = theme, Palette = palette };
            var window = new SettingsWindow(preferences, [new("", "Automatic")], 1, true, _ => Task.FromResult<string?>(null))
            { Width = 800, Height = 560 };
            window.Show();
            foreach (var section in palette == "Classic" ? new[] { 0, 1, 2, 3 } : new[] { 1 })
            {
                Get<ListBox>(window, "Sections").SelectedIndex = section;
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                var save = Get<Button>(window, "SaveSettings");
                var point = save.TranslatePoint(new Point(), window)!.Value;
                Assert.That(point.Y + save.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));
                Assert.That(point.X + save.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
                if (directory is not null) frame!.Save(Path.Combine(directory, $"settings-{theme}-{palette.Replace(' ', '-')}-{section}.png"), PngBitmapEncoderOptions.Default);
            }
            window.Close();
        }
    }
}
