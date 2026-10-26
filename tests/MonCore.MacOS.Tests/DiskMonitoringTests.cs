using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class DiskMonitoringTests
{
    [Test]
    public void ActivityEstimateUsesNanosecondsAndRequiresStableCompleteCounters()
    {
        var before = new MacDiskCounters("one", "disk0", 100, 100, null, 1000, 1000);
        var current = before with { ReadNanoseconds = 500001000, WriteNanoseconds = 500001000 };
        Assert.That(MacDiskSelection.ActivityPercent(before, current, 2, true), Is.EqualTo(50));
        Assert.That(MacDiskSelection.ActivityPercent(before, current, 0.5, true), Is.EqualTo(100), "Concurrent operations cap at 100");
        Assert.That(MacDiskSelection.ActivityPercent(current, current, 1, true), Is.Zero);
        Assert.That(MacDiskSelection.ActivityPercent(null, current, 1, true), Is.Null);
        Assert.That(MacDiskSelection.ActivityPercent(before, current, 1, false), Is.Null);
        Assert.That(MacDiskSelection.ActivityPercent(before, current with { Id = "replacement" }, 1, true), Is.Null);
        Assert.That(MacDiskSelection.ActivityPercent(before, current with { ReadNanoseconds = null }, 1, true), Is.Null);
        Assert.That(MacDiskSelection.ActivityPercent(before, current with { ReadNanoseconds = 0 }, 1, true), Is.Null);
        Assert.That(MacDiskSelection.ActivityPercent(before, current with { ReadNanoseconds = 0, WriteNanoseconds = 0 }, 1, true), Is.Null);
    }

    [Test]
    public void AggregateTimingRequiresEveryDriverAndBothGaugePercentagesKeepThroughputSeparate()
    {
        var first = Device("a", 100) with { ReadNanoseconds = 1000, WriteNanoseconds = 2000 };
        var second = Device("b", 100) with { ReadNanoseconds = 3000, WriteNanoseconds = 4000 };
        Assert.That(MacDiskSelection.Resolve(Activity(first, second), null, true).ReadNanoseconds, Is.EqualTo(4000));
        Assert.That(MacDiskSelection.Resolve(Activity(first, second with { ReadNanoseconds = null }), null, true).ReadNanoseconds, Is.Null);
        Assert.That(MacDiskSelection.Resolve(Activity(first with { ReadNanoseconds = ulong.MaxValue }, second), null, true).ReadNanoseconds, Is.Null);
        var sample = new MonitorSample(DateTimeOffset.UtcNow, 1, [], 0, false, Stopwatch.Frequency)
        { Resources = new() { DiskName = "disk0", SecondaryDiskName = "disk2", DiskActivePercent = 42,
            SecondaryDiskActivePercent = 17, DiskReadBytesPerSecond = 1024, DiskWriteBytesPerSecond = 1024,
            SecondaryDiskReadBytesPerSecond = 4096, SecondaryDiskWriteBytesPerSecond = 0 } };
        var gauges = new ResourceGaugeHistory(); gauges.Observe(sample);
        Assert.That(gauges.Readings[3].Text, Is.EqualTo("42.0%"));
        Assert.That(gauges.Readings[5].Text, Is.EqualTo("17.0%"));
        Assert.That(gauges.Readings[3].Maximum, Is.EqualTo(100));
        Assert.That(ResourceCharts.Value(sample, 3), Is.EqualTo(2048));
        Assert.That(ResourceCharts.Value(sample, 5), Is.EqualTo(4096));
        gauges.Observe(sample with { MonotonicTimestamp = 2 * Stopwatch.Frequency, Resources = sample.Resources with { DiskName = "replacement", DiskActivePercent = 5 } });
        Assert.That(gauges.Readings[3].Peak, Is.EqualTo(5));
    }

    private static MacDiskDevice Device(string id, ulong count, string generation = "one") => new(id, "disk1 · Test SSD", id + generation, count, count, 1000000);
    private static MacActivitySnapshot Activity(params MacDiskDevice[] devices) => MacActivitySnapshot.Empty with { StorageSet = "all", Read = 1000, Written = 1000, Disks = devices };
    [Test]
    public void SelectionDoesNotFollowAReusedBsdNameOrFallbackWhenDisconnected()
    {
        var original = Device("gpt:original", 100);
        var selected = MacDiskSelection.Resolve(Activity(original), original.Id, false);
        var next = MacDiskSelection.Resolve(Activity(Device(original.Id, 300)), original.Id, false);
        Assert.That(MacDiskSelection.Rate(selected, next, 2, true, true), Is.EqualTo(100));
        var missing = MacDiskSelection.Resolve(Activity(Device("gpt:replacement", 500)), original.Id, false);
        Assert.That(missing.Read, Is.Null);
        Assert.That(missing.Name, Does.Contain("disconnected"));
        Assert.That(MacDiskSelection.Rate(selected, missing, 1, true, true), Is.Null);
        var returned = MacDiskSelection.Resolve(Activity(Device(original.Id, 50, "two")), original.Id, false);
        Assert.That(MacDiskSelection.Rate(next, returned, 1, true, true), Is.Null);
        Assert.That(MacDiskSelection.Rate(returned, returned with { Read = 100 }, 1, true, true), Is.EqualTo(50));
        Assert.That(MacDiskSelection.Rate(selected, next, 1, false, true), Is.Null);
        Assert.That(MacDiskSelection.Resolve(Activity(original, original), original.Id, false).Read, Is.Null, "Cloned IDs are ambiguous");
        Assert.That(MacDiskSelection.Resolve(Activity(original), null, false).Read, Is.Null, "Off is not aggregate");
    }
    [Test]
    public void FrameSourceKeepsEachSelectedDiskBaselineSeparateFromCapacityAndAggregate()
    {
        SystemObservation Observation(int second, MacDiskDevice[] devices) => new(DateTimeOffset.UnixEpoch.AddSeconds(second),
            (second + 1L) * Stopwatch.Frequency, new(10, 10, (uint)(100 + second), 0), 4, 1000, "/data", 500, 1000) { Activity = Activity(devices) };
        using var source = new MacFrameSource(new Reader([Observation(0, [Device("a", 100), Device("b", 1000)]),
            Observation(1, [Device("a", 200), Device("b", 1300)]), Observation(2, [Device("a", 300), Device("b", 1600)]),
            Observation(3, [Device("a", 400)])])) { PrimaryDiskId = "a", SecondaryDiskId = "b" };
        Assert.That(source.Capture(new()).Full.Resources.DiskReadBytesPerSecond, Is.Null);
        var sample = source.Capture(new()).Full;
        Assert.That(sample.Resources.DiskReadBytesPerSecond, Is.EqualTo(100));
        Assert.That(sample.Resources.SecondaryDiskReadBytesPerSecond, Is.EqualTo(300));
        Assert.That(ResourceCharts.Value(sample, 3), Is.EqualTo(200));
        Assert.That(ResourceCharts.Value(sample, 5), Is.EqualTo(600));
        Assert.That(source.Volume.FreeBytes, Is.EqualTo(500));
        Assert.That(sample.Resources.DiskFreeBytes, Is.Null, "Filesystem free space must not be attributed to a physical disk");
        source.PrimaryDiskId = null;
        sample = source.Capture(new()).Full;
        Assert.That(sample.Resources.DiskReadBytesPerSecond, Is.Null, "Changing to aggregate requires a baseline");
        Assert.That(sample.Resources.SecondaryDiskReadBytesPerSecond, Is.EqualTo(300));
        sample = source.Capture(new()).Full;
        Assert.That(sample.Resources.SecondaryDiskReadBytesPerSecond, Is.Null, "Disconnected second disk cannot become aggregate");
    }
    [Test]
    public void DiskPreferencesPreserveDisconnectedChoicesAndRejectInvalidIds()
    {
        var prefs = new MacPreferences { PrimaryDiskId = "gpt:a", SecondaryDiskId = "gpt:b", VisibleMetrics = 63, FocusMetric = 5 };
        var merged = SettingsValues.From(prefs).ApplyTo(prefs, "boot");
        Assert.That(merged.PrimaryDiskId, Is.EqualTo("gpt:a")); Assert.That(merged.SecondaryDiskId, Is.EqualTo("gpt:b"));
        Assert.That(merged.FocusMetric, Is.EqualTo(5));
        Assert.That((prefs with { SecondaryDiskId = "gpt:a" }).Normalized("boot").SecondaryDiskId, Is.Null);
        Assert.That((prefs with { PrimaryDiskId = "bad\nvalue", SecondaryDiskId = new string('x', 513) }).Normalized("boot").SecondaryDiskId, Is.Null);
        Assert.That(ResourceCharts.DiskRate(ResourceChartTests.Samples()[10].Resources, true), Is.Null, "Legacy aggregate history is not a second disk");
    }
    [AvaloniaTest]
    public void SettingsRetainDisconnectedSelectionAndRejectTheSameDiskTwice()
    {
        var saved = false;
        var window = new SettingsWindow(new MacPreferences { PrimaryDiskId = "gone" }, [], 2, true,
            _ => { saved = true; return Task.FromResult<string?>(null); }, [new("present", "disk0 · SSD")]);
        var primary = window.FindControl<ComboBox>("PrimaryDiskChoice")!;
        var second = window.FindControl<ComboBox>("SecondaryDiskChoice")!;
        Assert.That(((MacDiskChoice)primary.SelectedItem!).Name, Does.Contain("disconnected"));
        primary.SelectedItem = primary.Items.OfType<MacDiskChoice>().Single(d => d.Id == "present");
        second.SelectedItem = second.Items.OfType<MacDiskChoice>().Single(d => d.Id == "present");
        window.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(saved, Is.False);
        Assert.That(window.FindControl<TextBlock>("SettingsStatus")!.Text, Does.Contain("different second disk"));
        window.Close();
    }
    private sealed class Reader(IEnumerable<SystemObservation> observations) : IMacSystemReader
    {
        private readonly Queue<SystemObservation> _queue = new(observations);
        public SystemObservation Read(string path, string? networkId = null) => _queue.Dequeue();
        public void Dispose() { }
    }
}
