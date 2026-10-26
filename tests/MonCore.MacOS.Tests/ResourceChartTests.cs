using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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
        Assert.That(ResourceCharts.Panels(new(600, 180), 12, ResourceChartMode.Focus, 0).Single().Metrics, Is.EqualTo(new[] { 2 }));
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
