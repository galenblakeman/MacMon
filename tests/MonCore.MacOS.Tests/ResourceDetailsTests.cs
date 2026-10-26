using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class ResourceDetailsTests
{
    private static MonitorSample Sample() => new(DateTimeOffset.Parse("2026-10-26T10:20:30-05:00"), 24, [], 0,
        false, Stopwatch.Frequency) { IntervalSeconds = 1, Resources = new() {
            CpuKernelPercent = 4, ProcessCount = 450, TotalMemoryBytes = 16UL << 30, UsedMemoryBytes = 10UL << 30,
            AvailableMemoryBytes = 6UL << 30, CachedBytes = 2UL << 30, NetworkId = "en1", NetworkName = "Wi-Fi",
            ReceiveBytesPerSecond = 1024, SendBytesPerSecond = 2048, SecondaryDiskReadBytesPerSecond = 4096,
            SecondaryDiskWriteBytesPerSecond = 0, DiskName = "/System/Volumes/Data", DiskFreeBytes = 30UL << 30,
            DiskTotalBytes = 460UL << 30 } };
    private static ResourceDetailSnapshot Snapshot(ResourceMetric metric, MonitorSample? sample = null)
    {
        sample ??= Sample();
        var history = new ResourceGaugeHistory(); history.Observe(sample);
        return ResourceDetails.Create(metric, sample, new(10UL << 30, 2UL << 30, 1UL << 30, "Normal"), history.Readings[(int)metric], 10);
    }
    private static string Row(ResourceDetailSnapshot model, string label) => model.Rows.Single(row => row.Label == label).Value;

    [Test]
    public void CpuUsesMachineCapacityAndDoesNotInventUserCpuFromInvalidSystemValues()
    {
        var model = Snapshot(ResourceMetric.Cpu);
        Assert.That(Row(model, "Current"), Is.EqualTo("24.0%"));
        Assert.That(Row(model, "User CPU"), Is.EqualTo("20.0%"));
        Assert.That(Row(model, "System CPU"), Is.EqualTo("4.0%"));
        Assert.That(Row(model, "Logical cores"), Is.EqualTo("10"));
        Assert.That(Row(model, "Enumerated processes"), Is.EqualTo("450"));
        Assert.That(model.Status, Does.Contain("10:20:30 -05:00"));
        var zero = Sample() with { TotalCpuPercent = 0, Resources = Sample().Resources with { CpuKernelPercent = 0 } };
        Assert.That(Row(Snapshot(ResourceMetric.Cpu, zero), "User CPU"), Is.EqualTo("0.0%"));
        var inconsistent = Sample() with { Resources = Sample().Resources with { CpuKernelPercent = 80 } };
        Assert.That(Row(Snapshot(ResourceMetric.Cpu, inconsistent), "User CPU"), Is.EqualTo("—"));
        var invalid = Sample() with { Resources = Sample().Resources with { CpuKernelPercent = double.NaN } };
        Assert.That(Row(Snapshot(ResourceMetric.Cpu, invalid), "System CPU"), Is.EqualTo("—"));
    }

    [Test]
    public void MemoryExplainsEstimatesAndRetainsKnownZeroCompressedBytes()
    {
        var model = Snapshot(ResourceMetric.Memory);
        Assert.That(Row(model, "Available estimate"), Is.EqualTo("6.0 GiB"));
        Assert.That(Row(model, "Cached"), Is.EqualTo("2.0 GiB"));
        Assert.That(Row(model, "Compressed storage"), Is.EqualTo("1.0 GiB"));
        Assert.That(Row(model, "Pressure"), Is.EqualTo("Normal"));
        Assert.That(model.Definition, Does.Contain("not guaranteed allocatable").And.Contain("live adapter metadata"));
        model = ResourceDetails.Create(ResourceMetric.Memory, Sample(), new(null, null, 0, "Unavailable"), GaugeReading.Empty, 10);
        Assert.That(Row(model, "Compressed storage"), Is.EqualTo("0.0 B"));
        Assert.That(Row(model, "Pressure"), Is.EqualTo("—"));
    }

    [Test]
    public void MissingLiveSampleSuppressesRestoredValuesPeaksAndLiveAdapterMetadata()
    {
        foreach (var metric in Enum.GetValues<ResourceMetric>())
        {
            var model = ResourceDetails.Create(metric, null, new(1, 2, 3, "Critical"), new(50, 75, 100, "50%", "0–100%"), 10);
            Assert.That(model.Status, Does.Contain("Waiting for live"));
            Assert.That(model.Rows.Where(row => row.Label != "Activity scope").All(row => row.Value == "—"), Is.True, metric.ToString());
        }
    }

    [Test]
    public void PartialNetworkReadingsKeepZeroSeparateFromUnknownAndDoNotInventUtilization()
    {
        var partial = Sample() with { Resources = Sample().Resources with { ReceiveBytesPerSecond = 0, SendBytesPerSecond = null } };
        var model = Snapshot(ResourceMetric.Network, partial);
        Assert.That(Row(model, "Receive"), Is.EqualTo("0.0 KiB/s"));
        Assert.That(Row(model, "Send"), Is.EqualTo("—"));
        Assert.That(Row(model, "Current"), Is.EqualTo("0.0 Kbps"));
        Assert.That(Row(model, "Peak · last 2 minutes"), Is.EqualTo("0.0 Kbps"));
        Assert.That(model.Definition, Does.Contain("not link speed or percent utilization"));
        Assert.That(Row(Snapshot(ResourceMetric.Network), "Current"), Is.EqualTo("8.2 Kbps"));
    }

    [Test]
    public void StorageSeparatesAggregateActivityFromFilesystemCapacity()
    {
        var model = Snapshot(ResourceMetric.Storage);
        Assert.That(Row(model, "Current"), Is.EqualTo("—"));
        Assert.That(Row(model, "Write"), Is.EqualTo("0.0 KiB/s"));
        Assert.That(Row(model, "Capacity volume"), Is.EqualTo("/System/Volumes/Data"));
        Assert.That(Row(model, "Available capacity"), Is.EqualTo("30.0 GiB"));
        Assert.That(model.Definition, Does.Contain("separate filesystem reading"));
        Assert.That(model.Rows.Any(row => row.Label.Contains("Queue") || row.Label.Contains("utilization")), Is.False);
    }

    [AvaloniaTest]
    public void ResourceTooltipIsLazyStaysLiveAndClosesWithEscapeOrDetach()
    {
        var calls = 0; var snapshot = Snapshot(ResourceMetric.Cpu);
        var gauge = new ResourceGauge { Title = "CPU", IsCompact = true, GaugeStyle = "None" };
        gauge.EnableDetails(() => { calls++; return snapshot; });
        var window = new Window { Width = 720, Height = 200, Content = gauge };
        window.Show(); Dispatcher.UIThread.RunJobs();
        gauge.Update(GaugeReading.Empty, "baseline");
        Assert.That(calls, Is.Zero);
        Assert.That(gauge.GetVisualDescendants().OfType<Button>(), Is.Empty, "Readouts no longer have Details buttons");
        gauge.Focus(); Dispatcher.UIThread.RunJobs();
        Assert.That(ToolTip.GetIsOpen(gauge), Is.True);
        Assert.That(calls, Is.GreaterThan(0));
        Assert.That(window.OwnedWindows, Is.Empty, "Details are a tooltip, not another window");
        var view = (ResourceDetailsView)ToolTip.GetTip(gauge)!;
        Assert.That(view.FindControl<TextBlock>("DetailTitle")!.Text, Does.Contain("CPU"));
        Assert.That(view.FindControl<Button>("CloseDetails")!.IsVisible, Is.False);
        snapshot = ResourceDetails.Create(ResourceMetric.Cpu, null, null, GaugeReading.Empty, 10);
        gauge.Update(GaugeReading.Empty, "Unavailable");
        Assert.That(view.FindControl<TextBlock>("DetailStatus")!.Text, Does.Contain("Waiting for live"));
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.That(ToolTip.GetIsOpen(gauge), Is.False);
        var closedCalls = calls;
        gauge.Update(GaugeReading.Empty, "Unavailable"); Assert.That(calls, Is.EqualTo(closedCalls));
        ToolTip.SetIsOpen(gauge, true); window.Content = null;
        Assert.That(ToolTip.GetIsOpen(gauge), Is.False);
        window.Close();
    }

    [AvaloniaTest]
    public void KeyboardResourceFocusSwitchesTooltipsWithoutDisablingOwner()
    {
        var compact = new CompactView();
        foreach (var (gauge, index) in compact.Gauges.Select((gauge, index) => (gauge, index)))
        {
            gauge.EnableDetails(() => Snapshot((ResourceMetric)index));
            gauge.DetailsOpening += () => { foreach (var other in compact.Gauges) other.HideDetails(); };
        }
        var window = new Window { Width = 720, Height = 224, Content = compact };
        window.Show(); Dispatcher.UIThread.RunJobs();
        foreach (var gauge in compact.Gauges.Take(2))
        {
            gauge.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.That(window.IsEnabled, Is.True);
            Assert.That(window.OwnedWindows, Is.Empty);
            Assert.That(compact.Gauges.Count(ToolTip.GetIsOpen), Is.EqualTo(1));
            Assert.That(ToolTip.GetIsOpen(gauge), Is.True);
        }
        window.Close();
    }

    [AvaloniaTest]
    public void DetailPanelsRenderBothThemesWithLongScopeAndReachableCloseButton()
    {
        var app = (App)Application.Current!;
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var metric in Enum.GetValues<ResourceMetric>())
        {
            app.ApplyAppearance(theme, "Ocean", "Bar");
            var view = new ResourceDetailsView();
            var model = Snapshot(metric);
            view.Update(model with { Rows = [.. model.Rows, new("Long scope", new string('x', 300))] });
            var window = new Window { Width = 388, Height = 540, Content = new Border { Padding = new Thickness(24), Child = view } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            var close = view.FindControl<Button>("CloseDetails")!;
            var origin = close.TranslatePoint(default, window)!.Value;
            Assert.That(origin.Y + close.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));
            Assert.That(origin.X + close.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
            Assert.That(view.Bounds.Height, Is.LessThanOrEqualTo(500));
            var value = view.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == model.Rows[0].Value);
            Assert.That(((ISolidColorBrush)value.Foreground!).Color, Is.EqualTo(Color.Parse(theme == "Light" ? "#17222E" : "#F2F5F8")));
            if (output is not null) frame!.Save(Path.Combine(output, $"details-{theme}-{metric}.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
        app.ApplyAppearance("Dark", "Classic");
    }
}
