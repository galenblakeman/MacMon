using Avalonia;
using Avalonia.Controls;
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
        var focus = main.FindControl<ComboBox>("FocusMetricBox")!;
        var chart = main.FindControl<CpuChart>("Chart")!;
        foreach (var name in new[] { "CpuVisible", "MemoryVisible", "NetworkVisible", "StorageVisible" })
            main.FindControl<CheckBox>(name)!.IsChecked = false;
        Assert.That(chart.VisibleMetrics, Is.Zero);
        mode.SelectedIndex = 3;
        focus.SelectedIndex = 2;
        Assert.That(chart.VisibleMetrics, Is.EqualTo(4));
        Assert.That(chart.FocusMetric, Is.EqualTo(2));
        Assert.That(chart.Mode, Is.EqualTo(ResourceChartMode.Focus));
        main.FindControl<CheckBox>("StorageVisible")!.IsChecked = true;
        main.FindControl<CheckBox>("NetworkVisible")!.IsChecked = false;
        Assert.That(chart.FocusMetric, Is.EqualTo(3));
        Assert.That(focus.SelectedIndex, Is.EqualTo(3));
        Assert.That(chart.VisibleMetrics, Is.EqualTo(8));
    }

    [AvaloniaTest]
    public void FullShellKeepsFourCardsAndDataPaneAtSupportedSizes()
    {
        var app = (App)Application.Current!;
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        foreach (var size in new[] { new Size(800, 760), new Size(1000, 900) })
        foreach (var theme in new[] { "Light", "Dark" })
        {
            app.ApplyAppearance(theme, "Classic", "Bowl");
            // Host the initialized shell without opening the collector-owning main window.
            var main = new MainWindow();
            app.ApplyAppearance(theme, "Classic", "Bowl");
            main.FindControl<TabControl>("DataTabs")!.SelectedIndex = 0;
            var shell = main.Content;
            main.Content = null;
            main.FindControl<Grid>("MainLayout")!.IsVisible = true;
            main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
            var window = new Window { Width = size.Width, Height = size.Height, Content = shell };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var gauges = window.GetVisualDescendants().OfType<ResourceGauge>().Where(g => !g.IsCompact).ToArray();
            Assert.That(gauges, Has.Length.EqualTo(4));
            Assert.That(gauges.Select(g => g.TranslatePoint(default, window)!.Value.Y).Distinct().Count(), Is.EqualTo(1));
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            Assert.That(tabs.Bounds.Height, Is.GreaterThanOrEqualTo(276));
            foreach (var chart in window.GetVisualDescendants().OfType<CpuChart>())
            {
                Assert.That(chart.Bounds.Width, Is.GreaterThan(200));
                Assert.That(chart.Bounds.Height, Is.GreaterThan(60));
            }
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            if (output is not null) frame!.Save(Path.Combine(output, $"shell-{size.Width}-{theme}.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
        app.ApplyAppearance("Dark", "Classic", "Bowl");
    }
}
