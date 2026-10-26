using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MacMon;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class ResourceChartTests
{
    internal static MonitorSample[] Samples() => Enumerable.Range(0, 121).Select(i => new MonitorSample(
        DateTimeOffset.UnixEpoch.AddSeconds(i), 40 + 30 * Math.Sin(i / 8d), [], 0,
        i == 45, (i + 1L) * Stopwatch.Frequency)
    {
        Resources = new() { TotalMemoryBytes = 100, UsedMemoryBytes = (ulong)(50 + i / 10), NetworkId = "en0",
            ReceiveBytesPerSecond = i == 75 ? null : (20 + 20 * Math.Sin(i / 5d)) * 1024,
            SendBytesPerSecond = 1024, SecondaryDiskReadBytesPerSecond = i * 1024 * 8,
            SecondaryDiskWriteBytesPerSecond = 0 }
    }).ToArray();

    [Test]
    public void ScalesUseVisibleDataAndKeepThroughputSeparateFromPercentages()
    {
        var samples = Samples();
        Assert.That(ResourceCharts.Maximum(samples, 120, 0), Is.EqualTo(100));
        Assert.That(ResourceCharts.Maximum(samples, 120, 1), Is.EqualTo(100));
        Assert.That(ResourceCharts.Maximum(samples, 120, 2), Is.EqualTo(65536));
        Assert.That(ResourceCharts.Maximum(samples, 120, 3), Is.EqualTo(1048576));
        Assert.That(ResourceCharts.Value(samples[45], 0), Is.Null);
        Assert.That(ResourceCharts.Value(samples[45], 1), Is.Not.Null);
        Assert.That(ResourceCharts.Value(samples[75], 2), Is.Null);
        var oldPeak = samples[0] with { Resources = samples[0].Resources with { ReceiveBytesPerSecond = 1e9 } };
        Assert.That(ResourceCharts.Maximum([oldPeak, .. samples.Skip(1)], 10, 2), Is.LessThan(1e9));
        Assert.That(ResourceCharts.Value(samples[0] with { TotalCpuPercent = double.NaN }, 0), Is.Null);
        Assert.That(ResourceCharts.Scale(2, 65536), Does.Contain("KiB/s").And.Not.Contain("%"));
    }

    [Test]
    public void ChartChoicesNormalizeAndSurviveSettingsEditsAndSerialization()
    {
        var original = new MacPreferences { VisibleMetrics = 12, ChartMode = ResourceChartMode.Focus, FocusMetric = 3 };
        var updated = SettingsValues.From(original).ApplyTo(original, null);
        Assert.That(updated.VisibleMetrics, Is.EqualTo(12));
        Assert.That(updated.ChartMode, Is.EqualTo(ResourceChartMode.Focus));
        Assert.That(updated.FocusMetric, Is.EqualTo(3));
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<MacPreferences>(System.Text.Json.JsonSerializer.Serialize(updated))!.Normalized(null);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(roundTrip), Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(updated)));
        var invalid = new MacPreferences { VisibleMetrics = 18, ChartMode = (ResourceChartMode)99, FocusMetric = -1 }.Normalized(null);
        Assert.That(invalid.VisibleMetrics, Is.EqualTo(2));
        Assert.That(invalid.FocusMetric, Is.EqualTo(1));
        Assert.That(invalid.ChartMode, Is.EqualTo(ResourceChartMode.Overlay));
        Assert.That(ResourceCharts.Panels(new(600, 180), 0, ResourceChartMode.Focus, 0), Is.Empty);
        Assert.That(ResourceCharts.Panels(new(600, 180), 12, ResourceChartMode.Focus, 0).First().Metrics, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void KeyboardNavigationClampsToVisibleSamplesAndHandlesExpiredSelections()
    {
        var samples = Samples();
        Assert.That(HistoryInspection.Navigate(samples, 10, null, -1), Is.SameAs(samples[^2]));
        Assert.That(HistoryInspection.Navigate(samples, 10, null, int.MinValue), Is.SameAs(samples[110]));
        Assert.That(HistoryInspection.Navigate(samples, 10, samples[0].MonotonicTimestamp, -1), Is.SameAs(samples[110]));
        Assert.That(HistoryInspection.Navigate(samples, 10, samples[^1].MonotonicTimestamp, 1), Is.SameAs(samples[^1]));
        Assert.That(HistoryInspection.Navigate([], 10, null, -1), Is.Null);
    }

    [AvaloniaTest]
    public void PointerPreviewDoesNotCaptureAndKeyboardWorksInEveryLayout()
    {
        foreach (var mode in Enum.GetValues<ResourceChartMode>())
        {
            var samples = Samples();
            var chart = new CpuChart { Mode = mode, FocusMetric = 2 };
            chart.Update(samples);
            MonitorSample? selected = null;
            var live = 0;
            chart.SampleSelected += sample => { selected = sample; chart.SelectedTimestamp = sample.MonotonicTimestamp; };
            chart.LiveRequested += () => { live++; selected = null; chart.SelectedTimestamp = null; };
            var window = new Window { Width = 720, Height = 220, Content = chart };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var panel = ResourceCharts.Panels(chart.Bounds.Size, 15, mode, 2).Last();
            var plot = ResourceCharts.Plot(panel);
            var point = new Point(plot.X + plot.Width / 2, plot.Y + plot.Height / 2);
            window.MouseMove(point);
            Assert.That(chart.HoveredSample, Is.SameAs(samples[60]), mode.ToString());
            Assert.That(selected, Is.Null, "Hover must not enter Capture mode");
            Assert.That(ToolTip.GetTip(chart)?.ToString(), Does.Contain("CPU").And.Contain("Network").And.Contain("Storage"));
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.That(selected, Is.SameAs(samples[60]));
            Assert.That(chart.IsFocused, Is.True);
            void Press(Key key)
            {
                window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            }
            Press(Key.Right);
            Assert.That(selected, Is.SameAs(samples[61]));
            Assert.That(chart.HoveredSample, Is.Null);
            Press(Key.Home); Assert.That(selected, Is.SameAs(samples[0]));
            Press(Key.Left); Assert.That(selected, Is.SameAs(samples[0]));
            Press(Key.End); Assert.That(selected, Is.SameAs(samples[^1]));
            Press(Key.Right); Assert.That(selected, Is.SameAs(samples[^1]));
            Press(Key.Escape); Assert.That(live, Is.EqualTo(1)); Assert.That(selected, Is.Null);
            window.MouseMove(new Point(-1, -1));
            Assert.That(chart.HoveredSample, Is.Null, "Leaving the chart clears preview");
            window.Close();
        }
    }

    [AvaloniaTest]
    public void CaptureMarkersUseTheirMetricPanelAndDisappearWithHiddenMetrics()
    {
        var samples = Samples();
        var record = new MacCaptureRecord(Guid.NewGuid(), SampleKey.Of(samples[60]), CaptureMetric.Network, "network", "unused");
        var chart = new CpuChart { Mode = ResourceChartMode.SmallMultiples, Captures = [record] };
        chart.Update(samples);
        var captures = 0; var selections = 0;
        chart.CaptureSelected += capture => { Assert.That(capture, Is.SameAs(record)); captures++; };
        chart.SampleSelected += _ => selections++;
        var window = new Window { Width = 720, Height = 220, Content = chart };
        window.Show(); Dispatcher.UIThread.RunJobs();
        void ClickPanel(int mask, int index)
        {
            var plot = ResourceCharts.Plot(ResourceCharts.Panels(chart.Bounds.Size, mask, chart.Mode, 0)[index]);
            var point = new Point(plot.X + plot.Width / 2, plot.Y + 3);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        }
        ClickPanel(15, 3);
        Assert.That(captures, Is.EqualTo(1)); Assert.That(selections, Is.Zero);
        chart.VisibleMetrics = 3; chart.Update(samples);
        ClickPanel(3, 0);
        Assert.That(captures, Is.EqualTo(1)); Assert.That(selections, Is.EqualTo(1));
        window.Close();
    }

    [Test]
    public void AlternativeChartsUseWinMonResourceOrderAndStackedLanes()
    {
        var stacked = ResourceCharts.Panels(new(800, 240), 15, ResourceChartMode.SmallMultiples, 0);
        Assert.That(stacked.Select(p => p.Metrics.Single()), Is.EqualTo(new[] { 0, 1, 3, 2 }));
        Assert.That(stacked.All(p => p.Bounds.X == 0 && p.Bounds.Width == 800), Is.True);
        Assert.That(stacked.Select(p => p.Bounds.Y), Is.Ordered.Ascending);
        var focus = ResourceCharts.Panels(new(800, 240), 15, ResourceChartMode.Focus, 2);
        Assert.That(focus[0].Metrics, Is.EqualTo(new[] { 2 }));
        Assert.That(focus[0].Bounds.Height, Is.GreaterThan(focus[1].Bounds.Height));
        Assert.That(focus.Skip(1).Select(p => p.Metrics.Single()), Is.EqualTo(new[] { 0, 1, 3 }));
    }

    [AvaloniaTest]
    public void AllChartModesRenderWithMissingIntervalsAndMetricMasks()
    {
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var mode in Enum.GetValues<ResourceChartMode>())
        foreach (var mask in new[] { 15, 4, 0 })
        {
            ((App)Application.Current!).ApplyAppearance(theme, "Classic", "Bowl");
            var chart = new CpuChart { Mode = mode, VisibleMetrics = mask, FocusMetric = 2, SelectedTimestamp = 90 * Stopwatch.Frequency };
            chart.Update(Samples());
            var window = new Window { Width = 720, Height = 220, Content = chart };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            foreach (var panel in ResourceCharts.Panels(chart.Bounds.Size, mask, mode, 2))
            {
                Assert.That(panel.Bounds.Right, Is.LessThanOrEqualTo(chart.Bounds.Width));
                Assert.That(ResourceCharts.Plot(panel).Height, Is.GreaterThan(10));
            }
            if (output is not null && mask == 15) frame!.Save(Path.Combine(output, $"chart-{mode}-{theme}.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
    }
}
