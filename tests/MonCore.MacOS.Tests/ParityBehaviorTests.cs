using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class ParityBehaviorTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PinsKeepInsertionOrderAcrossRankingAndMissingObservations(bool grouped)
    {
        var sample = TablePresentationTests.Sample(); var pins = new ProcessPins(MacFrameSource.IdentityPolicy);
        var first = sample.Processes[0]; var second = sample.Processes[^1];
        pins.Toggle(first, grouped); pins.Toggle(second, grouped);
        foreach (var ascending in new[] { true, false })
        foreach (var order in Enum.GetValues<ProcessTableOrder>())
        foreach (var sort in new[] { ProcessSort.Cpu, ProcessSort.Memory, ProcessSort.Io, ProcessSort.Threads })
        {
            var rows = ProcessPresentation.Rows([sample], sample, grouped, sort, "", pins, false,
                ascending: ascending, order: order, limit: 5);
            Assert.That(rows.Take(2).Select(row => row.Process.Identity), Is.EqualTo(new[] { first.Identity, second.Identity }));
        }
        var missing = sample with { Processes = sample.Processes.Skip(1).ToArray() };
        var retained = ProcessPresentation.Rows([missing], missing, grouped, ProcessSort.Cpu, "", pins, true);
        Assert.That(retained.Select(row => row.Process.Identity), Is.EqualTo(new[] { first.Identity, second.Identity }));
    }

    [AvaloniaTest]
    public void AccessiblePalettesUpdateAttachedChartsAndGiveEveryResourceADistinctPattern()
    {
        var app = (App)Application.Current!; var chart = new CpuChart(); var window = new Window { Content = chart };
        try
        {
            window.Show();
            foreach (var palette in new[] { "Colorblind", "High-Contrast & Inclusive", "Classic" })
            {
                app.ApplyAppearance("Dark", palette); Dispatcher.UIThread.RunJobs();
                Assert.That(chart.UseSeriesPatterns, Is.EqualTo(palette != "Classic"));
            }
            var patterns = Enumerable.Range(0, 6).Select(metric => CpuChart.SeriesPattern(metric, true, true))
                .Select(pattern => pattern is null ? "solid" : string.Join(",", pattern.Dashes ?? [])).ToArray();
            Assert.That(patterns.Distinct().Count(), Is.EqualTo(6));
            Assert.That(CpuChart.SeriesPattern(5, false, true), Is.Not.Null);
            Assert.That(CpuChart.SeriesPattern(5, false, false), Is.Null);
            if (Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT") is { } output)
            {
                Directory.CreateDirectory(output);
                window.Width = 1000; window.Height = 400; chart.VisibleMetrics = 63;
                chart.Update(Enumerable.Range(0, 60).Select(i => new MonitorSample(
                    DateTimeOffset.UnixEpoch.AddSeconds(i), 30 + 10 * Math.Sin(i / 5d), [], 0,
                    MonotonicTimestamp: (i + 1) * Stopwatch.Frequency)
                { Resources = new ResourceSample { TotalMemoryBytes = 1000000000, UsedMemoryBytes = (ulong)(550000000 + 50000000 * Math.Sin(i / 8d)),
                    GpuPercent = 75 + 8 * Math.Sin(i / 4d), ReceiveBytesPerSecond = 100000 + 50000 * Math.Sin(i / 7d),
                    DiskName = "disk0", DiskReadBytesPerSecond = 2000000 + 1000000 * Math.Sin(i / 6d), DiskWriteBytesPerSecond = 0,
                    SecondaryDiskName = "disk1", SecondaryDiskReadBytesPerSecond = 500000 + 300000 * Math.Sin(i / 9d), SecondaryDiskWriteBytesPerSecond = 0 } }).ToArray());
                foreach (var palette in new[] { "Colorblind", "High-Contrast & Inclusive" })
                {
                    app.ApplyAppearance("Dark", palette); Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(output, palette + ".png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally { window.Close(); app.ApplyAppearance("Dark", "Classic"); }
    }

    [AvaloniaTest]
    public void JobActionsFollowIdentityOutsideFiltersAndRefreshAcrossHostRestart()
    {
        var sample = TablePresentationTests.Sample(); var process = sample.Processes[0];
        var context = new LaunchdContext(Guid.NewGuid(), 501, "Aqua");
        var job = new LaunchdJob(new(context, "example.job"), process.ProcessId, 0, process.Identity);
        var snapshot = new LaunchdSnapshot(sample.Timestamp, context, [job]);
        var view = new LaunchdView(_ => Task.FromResult(snapshot));
        var window = new Window { Content = view };
        var monitor = (LaunchdMonitor)typeof(LaunchdView).GetField("_monitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        void Rebuild() => typeof(LaunchdView).GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
        try
        {
            window.Show(); monitor.Inventory.Apply(snapshot); view.ObserveLiveSample(sample); Rebuild();
            var list = view.FindControl<ListBox>("JobList")!;
            list.SelectedItem = ((IEnumerable<LaunchdRow>)list.ItemsSource!).Single();
            view.FindControl<TextBox>("JobSearch")!.Text = "hidden"; Dispatcher.UIThread.RunJobs();
            Assert.That(list.SelectedItem, Is.Null);
            Assert.That(view.FindControl<Button>("JobDetails")!.IsEnabled, Is.True);
            Assert.That(view.FindControl<Button>("CopyJob")!.IsEnabled, Is.True);
            Assert.That(view.FindControl<Button>("ShowWorkers")!.IsEnabled, Is.True);
            Assert.That(view.FindControl<TextBlock>("JobSelectionName")!.Text, Is.EqualTo(job.Id.Label));
            view.FindControl<TextBox>("JobSearch")!.Text = ""; Dispatcher.UIThread.RunJobs();
            view.FilterIndex = 2;
            Assert.That(list.SelectedItem, Is.Null);
            Assert.That(view.FindControl<Button>("JobDetails")!.IsEnabled, Is.True);
            view.FilterIndex = 0;
            monitor.Inventory.Apply(snapshot with { Jobs = Enumerable.Range(0, 6)
                .Select(i => new LaunchdJob(new(context, "aaa." + i), process.ProcessId, 0, process.Identity)).Append(job).ToArray() });
            view.RowLimit = 5; Rebuild();
            Assert.That(list.SelectedItem, Is.Null);
            Assert.That(view.FindControl<Button>("CopyJob")!.IsEnabled, Is.True);
            var replacement = process with { CreationTicks = process.CreationTicks + 1 };
            monitor.Inventory.Apply(snapshot with { Jobs = [job with { ObservedIdentity = replacement.Identity }] });
            Rebuild();
            Assert.That(view.FindControl<Button>("ShowWorkers")!.IsEnabled, Is.False, "Never use the old host's readings");
            view.ObserveLiveSample(sample with { Processes = [replacement] }); Rebuild();
            Assert.That(view.FindControl<Button>("ShowWorkers")!.IsEnabled, Is.True);
            monitor.Inventory.Apply(snapshot with { Jobs = [] }); Rebuild();
            Assert.That(view.FindControl<Button>("JobDetails")!.IsEnabled, Is.False);
        }
        finally { window.Close(); view.Stop().GetAwaiter().GetResult(); }
    }

    [Test]
    public void UpdateHandoffIsSingleUseAndRestoresUsageWithoutCountingLifetimesTwice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MacMon-update-" + Guid.NewGuid().ToString("N"));
        var sample = TablePresentationTests.Sample() with { Timestamp = DateTimeOffset.UtcNow };
        var ledger = new UsageLedger(sample.Timestamp, MacFrameSource.IdentityPolicy); ledger.Add(sample);
        try
        {
            MacUpdateRestart.Save(directory, "boot", sample.Timestamp, [new(sample, "boot")], ledger.Export());
            var restored = MacUpdateRestart.Consume(directory, "boot", 600, sample.Timestamp.AddSeconds(1), Stopwatch.GetTimestamp());
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.History.Entries, Has.Count.EqualTo(1));
            var resumed = new UsageLedger(sample.Timestamp, MacFrameSource.IdentityPolicy); resumed.Restore(restored.Usage);
            Assert.That(resumed.MachineBusyTicks, Is.EqualTo(ledger.MachineBusyTicks));
            Assert.That(resumed.Apps.Select(app => app.CpuTicks), Is.EqualTo(ledger.Apps.Select(app => app.CpuTicks)));
            Assert.That(resumed.Apps[0].LastActive, Is.EqualTo(SampleKey.Of(restored.History.Entries[0].Sample)));
            resumed.Add(sample with { Timestamp = sample.Timestamp.AddSeconds(2), MonotonicTimestamp = sample.MonotonicTimestamp + Stopwatch.Frequency });
            Assert.That(resumed.Apps.All(app => app.Lifetimes == 1), Is.True);
            Assert.That(resumed.Apps[0].CpuTicks, Is.EqualTo(ledger.Apps[0].CpuTicks * 2));
            Assert.That(MacUpdateRestart.Consume(directory, "boot", 600, sample.Timestamp, Stopwatch.GetTimestamp()), Is.Null);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase("other-boot", 1)]
    [TestCase("boot", 601)]
    [TestCase("boot", -1)]
    public void UpdateHandoffRejectsOtherBootsExpiredAndFutureState(string boot, int seconds)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MacMon-update-" + Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.UtcNow; var ledger = new UsageLedger(now, MacFrameSource.IdentityPolicy);
        try
        {
            MacUpdateRestart.Save(directory, "boot", now, [], ledger.Export());
            Assert.That(MacUpdateRestart.Consume(directory, boot, 600, now.AddSeconds(seconds), Stopwatch.GetTimestamp()), Is.Null);
            Assert.That(Directory.Exists(directory), Is.False);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BrokenUpdateHandoffFallsBackWithoutLeavingAnImportableManifest(bool missingHistory)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MacMon-update-" + Guid.NewGuid().ToString("N"));
        var now = DateTimeOffset.UtcNow; var ledger = new UsageLedger(now, MacFrameSource.IdentityPolicy);
        try
        {
            MacUpdateRestart.Save(directory, "boot", now, [], ledger.Export());
            Assert.Throws<InvalidDataException>(() => MacUpdateRestart.Save(directory, "boot", now, [],
                ledger.Export() with { IdentityPolicyId = "wrong-platform" }));
            Assert.That(File.Exists(Path.Combine(directory, "manifest.json")), Is.True, "A failed save preserves the complete handoff");
            if (missingHistory) File.Delete(Path.Combine(directory, "history.gz"));
            else File.WriteAllText(Path.Combine(directory, "manifest.json"), "{ broken JSON");
            Assert.That(MacUpdateRestart.Consume(directory, "boot", 600, now, Stopwatch.GetTimestamp()), Is.Null);
            Assert.That(Directory.Exists(directory), Is.False);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
