using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class CompactViewTests
{
    [Test]
    public void CompactResizeAndSettingsSavePreserveFullDimensionsAndViews()
    {
        var preferences = new MacPreferences { Width = 1200, Height = 900, CompactWidth = 850,
            DataTab = 2, HistoryRange = 2, ProcessSort = 3, PinnedOnly = true, DashboardRatio = 3 };
        var state = new WindowViewState(preferences);
        state.ObserveSize(new(1300, 950), true);
        state.SetMode(true);
        state.ObserveSize(new(780, WindowViewState.CompactHeight), true);
        state.ObserveSize(new(2000, 1200), false); // Maximized/minimized bounds must not replace normal bounds.
        var saved = SettingsValues.From(preferences).ApplyTo(state.ApplyTo(preferences), null);
        Assert.Multiple(() =>
        {
            Assert.That(saved.CompactMode, Is.True);
            Assert.That(saved.CompactWidth, Is.EqualTo(780));
            Assert.That(saved.Width, Is.EqualTo(1300));
            Assert.That(saved.Height, Is.EqualTo(950));
            Assert.That(saved.DataTab, Is.EqualTo(2));
            Assert.That(saved.HistoryRange, Is.EqualTo(2));
            Assert.That(saved.ProcessSort, Is.EqualTo(3));
            Assert.That(saved.PinnedOnly, Is.True);
            Assert.That(saved.DashboardRatio, Is.EqualTo(3));
        });
        var restored = new WindowViewState(saved);
        Assert.That(restored.CompactMode, Is.True);
        restored.SetMode(false);
        Assert.That(restored.FullSize, Is.EqualTo(new Size(1300, 950)));
        restored.SetMode(true);
        Assert.That(restored.CompactWidth, Is.EqualTo(780));
    }

    [Test]
    public void CompactPreferencesRoundTripAndInvalidWidthUsesSafeBounds()
    {
        Assert.That(new MacPreferences().CompactMode, Is.False);
        foreach (var width in new[] { double.NaN, double.PositiveInfinity, -1, 9999 })
        {
            var normalized = new MacPreferences { CompactWidth = width }.Normalized(null);
            Assert.That(normalized.CompactWidth, Is.InRange(720, 2400));
        }
        var root = Path.Combine(Path.GetTempPath(), "macmon-compact-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "settings.json");
            var expected = new MacPreferences { CompactMode = true, CompactWidth = 770, Width = 1100, Height = 880 };
            MacPreferenceStore.Save(path, expected);
            var restored = MacPreferenceStore.Load(path, null).Preferences;
            Assert.That(restored.CompactMode, Is.True);
            Assert.That(restored.CompactWidth, Is.EqualTo(770));
            Assert.That(restored.Width, Is.EqualTo(1100));
            Assert.That(restored.Height, Is.EqualTo(880));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTest]
    public void FullViewRestoresUnscaledLayoutAfterRepeatedCompactTransitions()
    {
        var main = new MainWindow();
        foreach (var field in new[] { "_preferencesWritable", "_historyWritable" })
            typeof(MainWindow).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(main, false);
        try
        {
            main.Show(); Dispatcher.UIThread.RunJobs();
            if (main.FindControl<CompactView>("CompactPanel")!.IsVisible)
                main.FindControl<CompactView>("CompactPanel")!.FindControl<Button>("ExpandView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            main.Width = 1000; main.Height = 900; Dispatcher.UIThread.RunJobs();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                main.FindControl<Button>("EnterCompactView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.That(main.ClientSize.Height, Is.EqualTo(WindowViewState.CompactHeight));
                main.FindControl<CompactView>("CompactPanel")!.FindControl<Button>("ExpandView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.That(main.ClientSize, Is.EqualTo(new Size(1000, 900)));
                Assert.That(main.FindControl<Grid>("TitleActions")!.Bounds.Height, Is.EqualTo(36));
                Assert.That(main.FindControl<ResourceGauge>("CpuGauge")!.Bounds.Height, Is.EqualTo(98));
                Assert.That(main.FindControl<Grid>("MainLayout")!.IsVisible, Is.True);
            }
        }
        finally { main.Close(); }
    }

    [AvaloniaTest]
    public void CompactActionsForwardAndUnavailableValuesNeverRetainOldMarks()
    {
        var view = new CompactView();
        var full = 0; var settings = 0; var top = false;
        view.FullViewRequested += () => full++;
        view.SettingsRequested += () => settings++;
        view.TopmostChanged += value => top = value;
        view.FindControl<Button>("ExpandView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.FindControl<Button>("CompactSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.AlwaysOnTop = true;
        Assert.That(full, Is.EqualTo(1));
        Assert.That(settings, Is.EqualTo(1));
        Assert.That(top, Is.True);
        foreach (var gauge in view.Gauges)
        {
            gauge.Update(new(50, 80, 100, "50.0%", "0–100%"), "live detail");
            Assert.That(AutomationProperties.GetName(gauge), Does.Contain("80.0%"));
            gauge.Update(GaugeReading.Empty, "Waiting for live readings");
            Assert.That(AutomationProperties.GetName(gauge), Does.Contain("Waiting for live readings").And.Not.Contain("50.0%"));
        }
    }

    private static IEnumerable<TestCaseData> CompactAppearances() =>
        from theme in new[] { "Light", "Dark" }
        from palette in Appearance.Palettes
        from style in ResourceGauge.GaugeStyles
        select new TestCaseData(theme, palette, style);

    [AvaloniaTest, TestCaseSource(nameof(CompactAppearances))]
    public void CompactStripRendersEveryStyleThemeAndPaletteAtMinimumAndDefaultWidths(string theme, string palette, string style)
    {
        var app = (App)Application.Current!;
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        Window? window = null;
        try
        {
            // Report each appearance independently and reuse its controls across layouts.
            // Recreating the window for the entire matrix made one test take minutes.
            app.ApplyAppearance(theme, palette, style);
            var view = new CompactView { Status = "Live · updated 12:34:56 · peak markers show the last 2 minutes" };
            view.Gauges[4].Update(new(42, 80, 100, "42.0%", "0–100%"), "GPU device");
            view.Gauges[5].Update(new(1023.9 * 1024, 1048576, 2097152, "1023.9 KiB/s", "0–2.0 MiB/s"), "disk1 · External SSD");
            view.Gauges[0].Update(new(24, 67, 100, "24.0%", "0–100%"), "Machine capacity");
            view.Gauges[1].Update(new(62, 72, 100, "62.0%", "0–100%"), "Physical usage");
            view.Gauges[2].Update(new(999.9 * 1024, 1048576, 2097152, "999.9 KiB/s", "0–2.0 MiB/s"), "en1 · receive + send");
            view.Gauges[3].Update(new(0, 1048576, 2097152, "0.0 KiB/s", "0–2.0 MiB/s"), "All block drivers · read + write");
            foreach (var (gauge, index) in view.Gauges.Select((gauge, index) => (gauge, index)))
                gauge.EnableDetails(() => ResourceDetails.Create((ResourceMetric)index, null, null, GaugeReading.Empty, 10));
            window = new Window { Width = 720, Height = WindowViewState.CompactHeight, Content = view };
            window.Show();
            foreach (var width in new[] { 720d, 1000d })
            foreach (var secondDisk in new[] { false, true })
            {
                window.Width = width;
                view.SetSecondDiskVisible(secondDisk);
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                foreach (var gauge in view.Gauges.Where(g => g.IsEffectivelyVisible))
                {
                    Assert.That(gauge.GaugeStyle, Is.EqualTo(style));
                    var origin = gauge.TranslatePoint(default, window)!.Value;
                    Assert.That(origin.Y + gauge.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));
                    Assert.That(origin.X + gauge.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
                    Assert.That(gauge.Bounds.Height, Is.EqualTo(98));
                    var number = gauge.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("gaugeValue"));
                    Assert.That(number.TextLayout.WidthIncludingTrailingWhitespace, Is.LessThanOrEqualTo(number.Bounds.Width), $"{width}/{theme}/{palette}/{style}");
                }
                var button = view.FindControl<Button>("ExpandView")!;
                Assert.That(button.TranslatePoint(default, window)!.Value.Y + button.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));
                if (!secondDisk && output is not null && palette == "Classic") frame!.Save(Path.Combine(output, $"compact-{width}-{theme}-{style}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window?.Close();
            app.ApplyAppearance("Dark", "Classic", "Bowl");
        }
    }
}
