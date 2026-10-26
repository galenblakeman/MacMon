using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class ChartReadabilityTests
{
    [AvaloniaTest]
    public void TwelvePointChartLabelsReserveSpaceForEveryTickInBothLanguages()
    {
        var chart = new CpuChart { VisibleMetrics = 63 };
        chart.Update(ResourceChartTests.Samples());
        var window = new Window { Width = 800, Height = 400, Content = chart };
        try
        {
            window.Show();
            foreach (var language in new[] { "en", "es" })
            foreach (var mode in Enum.GetValues<ResourceChartMode>())
            {
                UiText.Apply(language); chart.Mode = mode; Dispatcher.UIThread.RunJobs();
                var metrics = ResourceCharts.Metrics(chart.VisibleMetrics);
                var labels = mode == ResourceChartMode.Overlay
                    ? ResourceCharts.AxisLabels(metrics[0], chart.Maximum(metrics[0])).AsEnumerable()
                    : metrics.Select(i => UiText.Get(ResourceCharts.Names[i]))
                        .Concat(metrics.SelectMany(i => ResourceCharts.AxisLabels(i, chart.Maximum(i))));
                var width = Math.Ceiling(labels.Max(label => new FormattedText(label, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter, Arial"), 12, Brushes.White).Width) + 10);
                Assert.That(chart.Panels.All(panel => panel.LabelWidth == width), Is.True, language + "/" + mode);
                Assert.That(chart.Panels.All(panel => ResourceCharts.Plot(panel).Width > 100), Is.True);
            }
        }
        finally { window.Close(); UiText.Apply("en"); }
    }

    [AvaloniaTest]
    public void InactiveTabsKeepReadableContrastAndSelectionAcrossEveryTheme()
    {
        var main = new MainWindow();
        main.FindControl<Grid>("MainLayout")!.IsVisible = true;
        main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
        main.FindControl<CpuChart>("Chart")!.Update(ResourceChartTests.Samples());
        var shell = main.Content; main.Content = null;
        var window = new Window { Width = 800, Height = 760, Content = shell };
        var app = (App)Application.Current!;
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        try
        {
            window.Show();
            var tabs = main.FindControl<TabControl>("DataTabs")!;
            foreach (var theme in Appearance.Themes)
            {
                app.ApplyAppearance(theme, Appearance.DefaultPalette(theme)); Dispatcher.UIThread.RunJobs();
                Color Resource(string name) => ((ISolidColorBrush)app.Resources[name]!).Color;
                foreach (var index in new[] { 0, 1, 2 })
                {
                    tabs.SelectedIndex = index; Dispatcher.UIThread.RunJobs();
                    if (index == 0 && output is not null && theme is "Candy" or "Copper" or "Crystal" or "Slate")
                    {
                        using var frame = window.CaptureRenderedFrame();
                        Assert.That(frame, Is.Not.Null);
                        frame!.Save(Path.Combine(output!, $"readability-{theme}.png"), PngBitmapEncoderOptions.Default);
                    }
                    var headers = tabs.GetVisualDescendants().OfType<TabItem>().ToArray();
                    Assert.That(headers.Length, Is.EqualTo(3));
                    foreach (var tab in headers)
                    {
                        var color = ((ISolidColorBrush)tab.Foreground!).Color;
                        Assert.That(color, Is.EqualTo(Resource(tab.IsSelected ? "MacTextBrush" : "MacInactiveTabBrush")), theme);
                        if (tab.IsSelected) continue;
                        Assert.That(Appearance.Contrast(color, Resource("MacHeaderBrush")), Is.GreaterThanOrEqualTo(4.5), theme);
                        Assert.That(Appearance.Contrast(color, Resource("MacWindowBrush")), Is.GreaterThanOrEqualTo(4.5), theme);
                    }
                }
            }
        }
        finally { window.Close(); app.ApplyAppearance("Dark", "Classic"); }
    }
}
