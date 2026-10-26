using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class ShellLayoutTests
{
    [AvaloniaTest]
    public void FocusAndVisibilityControlsStayConsistentWhenMetricsAreHidden()
    {
        var main = new MainWindow();
        var mode = main.FindControl<ComboBox>("ChartModeBox")!;
        var chart = main.FindControl<CpuChart>("Chart")!;
        foreach (var name in new[] { "CpuGauge", "MemoryGauge", "NetworkGauge", "StorageGauge", "GpuGauge", "SecondDiskGauge" })
            main.FindControl<ResourceGauge>(name)!.SeriesToggle.IsChecked = false;
        Assert.That(chart.VisibleMetrics, Is.Zero);
        mode.SelectedIndex = 6; // Combined Focus · Network choice.
        Assert.That(chart.VisibleMetrics, Is.EqualTo(4));
        Assert.That(chart.FocusMetric, Is.EqualTo(2));
        Assert.That(chart.Mode, Is.EqualTo(ResourceChartMode.Focus));
        main.FindControl<ResourceGauge>("StorageGauge")!.SeriesToggle.IsChecked = true;
        main.FindControl<ResourceGauge>("NetworkGauge")!.SeriesToggle.IsChecked = false;
        Assert.That(chart.FocusMetric, Is.EqualTo(3));
        Assert.That(mode.SelectedIndex, Is.EqualTo(5));
        Assert.That(chart.VisibleMetrics, Is.EqualTo(8));
    }

    [AvaloniaTest]
    public void TimeChipsAndReorderedTabSummariesMatchTheirViews()
    {
        var main = new MainWindow();
        var chart = main.FindControl<CpuChart>("Chart")!;
        main.FindControl<RadioButton>("Range30")!.IsChecked = true;
        Assert.That(chart.RangeSeconds, Is.EqualTo(1800));
        Assert.That(main.FindControl<RadioButton>("Range2")!.IsChecked, Is.False);
        var tabs = main.FindControl<TabControl>("DataTabs")!;
        Assert.That(tabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString()), Is.EqualTo(new[] { "Processes", "Jobs", "Usage" }.Select(UiText.Get)));
        foreach (var index in new[] { 0, 1, 2 })
        {
            tabs.SelectedIndex = index;
            Assert.That(main.FindControl<TextBlock>("ProcessCountLabel")!.IsVisible, Is.EqualTo(index == 0));
            Assert.That(main.FindControl<TextBlock>("JobSummary")!.IsVisible, Is.EqualTo(index == 1));
            Assert.That(main.FindControl<TextBlock>("UsageStatus")!.IsVisible, Is.EqualTo(index == 2));
        }
    }

    [AvaloniaTest]
    public void TitleExportAndAutomaticCapturePopupsOpenFromActualInput()
    {
        var main = new MainWindow();
        main.FindControl<Grid>("MainLayout")!.IsVisible = true;
        main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
        var shell = main.Content; main.Content = null;
        var window = new Window { Width = 800, Height = 900, Content = shell };
        window.Show(); Dispatcher.UIThread.RunJobs();
        foreach (var name in new[] { "OpenExport", "AutomaticCaptures" })
        {
            var button = main.FindControl<Button>(name)!;
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.That(button.Flyout!.IsOpen, Is.True, name);
            var flyout = (Flyout)button.Flyout;
            Assert.That(((Control)flyout.Content!).GetVisualDescendants().OfType<Button>().Any(), Is.True);
            button.Flyout.Hide();
            Assert.That(button.Flyout.IsOpen, Is.False);
        }
        window.Close();
    }

    [AvaloniaTest]
    public void FullShellKeepsOptionalCardsAndDataPaneAtSupportedSizes()
    {
        var app = (App)Application.Current!;
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        foreach (var size in new[] { new Size(800, 760), new Size(1000, 900) })
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var secondDisk in new[] { false, true })
        {
            app.ApplyAppearance(theme, "Classic", "Bowl");
            // Host the initialized shell without opening the collector-owning main window.
            var main = new MainWindow();
            app.ApplyAppearance(theme, "Classic", "Bowl");
            main.FindControl<TabControl>("DataTabs")!.SelectedIndex = 0;
            main.FindControl<Border>("SecondDiskCard")!.IsVisible = secondDisk;
            main.FindControl<Avalonia.Controls.Primitives.UniformGrid>("ResourceCards")!.Columns = secondDisk ? 6 : 5;
            var history = ResourceChartTests.Samples();
            main.FindControl<CpuChart>("Chart")!.Update(history);
            var peaks = new ResourceGaugeHistory(); foreach (var sample in history) peaks.Observe(sample);
            foreach (var (name, metric) in new[] { ("CpuGauge", 0), ("MemoryGauge", 1), ("NetworkGauge", 2), ("StorageGauge", 3) })
                main.FindControl<ResourceGauge>(name)!.Update(peaks.Readings[metric], metric == 1 ? "8.0 / 16.0 GiB" : metric == 0 ? "Machine capacity" : "Read + write · receive + send");
            main.FindControl<TextBlock>("ProcessCountLabel")!.Text = "Processes 850 · observed threads 4,816";
            var shell = main.Content;
            main.Content = null;
            main.FindControl<Grid>("MainLayout")!.IsVisible = true;
            main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
            var window = new Window { Width = size.Width, Height = size.Height, Content = shell };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var gauges = window.GetVisualDescendants().OfType<ResourceGauge>().Where(g => !g.IsCompact && g.IsEffectivelyVisible).ToArray();
            Assert.That(gauges, Has.Length.EqualTo(secondDisk ? 6 : 5));
            Assert.That(gauges.Select(g => g.TranslatePoint(default, window)!.Value.Y).Distinct().Count(), Is.EqualTo(1), string.Join("; ", gauges.Select(g => $"{g.Title}: {g.TranslatePoint(default, window)} {g.Bounds}")));
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            Assert.That(tabs.Bounds.Height, Is.GreaterThanOrEqualTo(276));
            foreach (var chart in window.GetVisualDescendants().OfType<CpuChart>())
            {
                Assert.That(chart.Bounds.Width, Is.GreaterThan(200));
                Assert.That(chart.Bounds.Height, Is.GreaterThan(60));
            }
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            if (output is not null) frame!.Save(Path.Combine(output, $"shell-{size.Width}-{theme}{(secondDisk ? "-disk2" : "")}.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
        app.ApplyAppearance("Dark", "Classic", "Bowl");
    }
}
