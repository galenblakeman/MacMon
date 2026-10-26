using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class LaunchdDepthTests
{
    private static LaunchdContext Context() => new(Guid.NewGuid(), 501, "Aqua");
    private static ProcessUsage Process(int pid, long start, int parent = 0) => new(pid, start, "Worker " + pid, 3)
    { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ParentProcessId = parent, ImagePath = "/worker/" + pid,
        WorkingSetBytes = 1048576, IoBytesPerSecond = 1000000 };
    private static MonitorSample Sample(params ProcessUsage[] processes) => new(DateTimeOffset.UnixEpoch, 20, processes, 0,
        MonotonicTimestamp: Stopwatch.Frequency);
    private static LaunchdSnapshot Snapshot(LaunchdContext context, params LaunchdJob[] jobs) => new(DateTimeOffset.UnixEpoch, context, jobs);
    [Test]
    public void HistoryNeverUsesFutureOrStaleInventoryAndFreezesTheObservedJobSet()
    {
        var context = Context(); var jobs = new List<LaunchdJob> { new(new(context, "old"), 42, 0) };
        var history = new LaunchdHistory(); var first = new LaunchdSnapshot(DateTimeOffset.UnixEpoch, context, jobs);
        history.Record(first, 10 * Stopwatch.Frequency); jobs.Clear();
        Assert.That(history.At(9 * Stopwatch.Frequency), Is.Null);
        Assert.That(history.At(10 * Stopwatch.Frequency)!.Jobs.Single().Id.Label, Is.EqualTo("old"));
        Assert.That(history.At(25 * Stopwatch.Frequency), Is.Not.Null);
        Assert.That(history.At(26 * Stopwatch.Frequency), Is.Null);
        history.Record(Snapshot(context, new LaunchdJob(new(context, "new"), 43, 0)), 30 * Stopwatch.Frequency);
        Assert.That(history.At(20 * Stopwatch.Frequency)!.Jobs.Single().Id.Label, Is.EqualTo("old"));
        Assert.That(history.At(30 * Stopwatch.Frequency)!.Jobs.Single().Id.Label, Is.EqualTo("new"));
        history.Record(Snapshot(Context()), 40 * Stopwatch.Frequency);
        Assert.That(history.At(30 * Stopwatch.Frequency), Is.Null, "A changed bootstrap context must not join old observations");
        history.Record(Snapshot(context), Stopwatch.Frequency);
        Assert.That(history.Count, Is.EqualTo(1), "A clock reset must clear old observations");
    }
    [Test]
    public void HistoryBoundsBothObservationCountAndTotalJobReferences()
    {
        var context = Context(); var history = new LaunchdHistory();
        for (var i = 1; i <= 150; i++) history.Record(Snapshot(context), i * Stopwatch.Frequency);
        Assert.That(history.Count, Is.EqualTo(LaunchdHistory.MaximumSnapshots));
        var jobs = Enumerable.Range(0, 20000).Select(i => new LaunchdJob(new(context, "job" + i), null, 0)).ToArray();
        for (var i = 151; i <= 160; i++) history.Record(Snapshot(context, jobs), i * Stopwatch.Frequency);
        Assert.That(history.JobReferences, Is.LessThanOrEqualTo(LaunchdHistory.MaximumJobReferences));
        Assert.That(history.Count, Is.EqualTo(5));
    }
    [Test]
    public void WorkerTreeRequiresOrderedNativeLifetimesAndExcludesOtherJobsAndReusedParents()
    {
        var context = Context(); var root = Process(42, 100, 44); var child = Process(43, 200, 42);
        var grandchild = Process(44, 300, 43); var anotherJob = Process(47, 400, 42);
        var job = new LaunchdJob(new(context, "root"), 42, 0, root.Identity);
        var snapshot = Snapshot(context, job, new(new(context, "other"), 47, 0, anotherJob.Identity));
        var sample = Sample(root, child, grandchild, Process(45, 50, 42),
            Process(46, 500, 42) with { LifetimeDomain = ProcessLifetimeDomain.WindowsFileTime }, anotherJob, Process(48, 500, 47));
        var tree = LaunchdWorkers.Build(job, snapshot, sample, true);
        Assert.That(tree.Root!.Children.Single().Process.Identity, Is.EqualTo(child.Identity));
        Assert.That(tree.Root.Children.Single().Children.Single().Process.Identity, Is.EqualTo(grandchild.Identity));
        Assert.That(tree.Text(), Does.Not.Contain("Worker 45").And.Not.Contain("Worker 46").And.Not.Contain("Worker 47"));
        Assert.That(LaunchdWorkers.Build(job, snapshot, sample, false).Root, Is.Null);
        Assert.That(LaunchdWorkers.Build(job with { ObservedIdentity = Process(42, 999).Identity }, snapshot, sample, true).Root, Is.Null);
    }
    [Test]
    public void WorkerTreeHasNodeAndDepthLimits()
    {
        var context = Context(); var root = Process(42, 100); var job = new LaunchdJob(new(context, "root"), 42, 0, root.Identity);
        var tree = LaunchdWorkers.Build(job, Snapshot(context, job), Sample([root, .. Enumerable.Range(100, 1000).Select(i => Process(i, i + 1000, 42))]), true);
        Assert.That(tree.Root!.Children.Count + 1, Is.EqualTo(LaunchdWorkers.MaximumNodes)); Assert.That(tree.Truncated, Is.True);
        var chain = new List<ProcessUsage> { root };
        for (var i = 1; i <= 30; i++) chain.Add(Process(42 + i, 100 + i, 41 + i));
        var deep = LaunchdWorkers.Build(job, Snapshot(context, job), Sample(chain.ToArray()), true);
        var depth = 0; var node = deep.Root!;
        while (node.Children.Count > 0) { depth++; node = node.Children.Single(); }
        Assert.That(depth, Is.EqualTo(LaunchdWorkers.MaximumDepth)); Assert.That(deep.Truncated, Is.True);
    }
    [Test]
    public void CapturedInventoryJoinsOnlyTheCapturedProcessLifetime()
    {
        var context = Context(); var process = Process(42, 100); var job = new LaunchdJob(new(context, "root"), 42, 0, process.Identity);
        var captured = Sample(process); var snapshot = Snapshot(context, job);
        Assert.That(LaunchdPresentation.Rows(snapshot, captured, true, "", 0, 0).Single().Process, Is.EqualTo(process));
        Assert.That(LaunchdPresentation.Rows(snapshot, Sample(process with { CreationTicks = 999 }), true, "", 0, 0).Single().Process, Is.Null);
        Assert.That(LaunchdPresentation.Rows(snapshot, captured with { Processes = [] }, true, "", 0, 0).Single().Cpu, Is.EqualTo("—"));
    }
    [AvaloniaTest]
    public void MissingCapturedInventoryShowsNoLiveRowsAndDisablesRefresh()
    {
        var view = new LaunchdView(); var list = view.FindControl<ListBox>("JobList")!;
        var context = Context(); list.ItemsSource = LaunchdPresentation.Rows(Snapshot(context, new LaunchdJob(new(context, "live"), 42, 0)), null, false, "", 0, 0);
        view.SetInspection(Sample(), true);
        Assert.That(list.ItemCount, Is.Zero); Assert.That(view.FindControl<Button>("RefreshJobs")!.IsEnabled, Is.False);
        Assert.That(view.FindControl<TextBlock>("JobEmpty")!.Text, Does.Contain("No retained job inventory"));
        view.SetInspection(null, false); Assert.That(view.FindControl<Button>("RefreshJobs")!.IsEnabled, Is.True);
    }
    [AvaloniaTest]
    public async Task CapturedJobMetricsAndTrailsStayFrozenAsLiveInventoryAndSamplesChange()
    {
        var context = Context(); var process = Process(42, 100);
        var job = new LaunchdJob(new(context, "root"), 42, 0, process.Identity);
        var current = Snapshot(context, job);
        var view = new LaunchdView(_ => Task.FromResult(current));
        try
        {
            view.ObserveLiveSample(Sample(process) with { MonotonicTimestamp = Stopwatch.GetTimestamp() });
            await view.Refresh();
            var captured = Sample(process) with { MonotonicTimestamp = Stopwatch.GetTimestamp() };
            view.ObserveLiveSample(captured); view.SetInspection(captured, true);
            var list = view.FindControl<ListBox>("JobList")!;
            var frozen = ((IEnumerable<LaunchdRow>)list.ItemsSource!).Single();
            var trail = frozen.Trail.Values.ToArray();
            Assert.That(frozen.Cpu, Is.EqualTo("3.0%"));
            Assert.That(view.FindControl<Button>("RefreshJobs")!.IsEnabled, Is.False);
            var replacement = process with { CreationTicks = 999, CpuPercent = 99 };
            current = Snapshot(context, job with { ObservedIdentity = replacement.Identity });
            view.ObserveLiveSample(Sample(replacement) with { MonotonicTimestamp = Stopwatch.GetTimestamp() });
            await view.Refresh();
            for (var i = 1; i <= 40; i++)
                view.ObserveLiveSample(Sample(replacement) with { MonotonicTimestamp = captured.MonotonicTimestamp + i * Stopwatch.Frequency });
            view.SortAscending = true;
            var stillCaptured = ((IEnumerable<LaunchdRow>)list.ItemsSource!).Single();
            Assert.That(stillCaptured.Cpu, Is.EqualTo("3.0%")); Assert.That(stillCaptured.Trail.Values, Is.EqualTo(trail));
            view.SetInspection(null, false);
            Assert.That(((IEnumerable<LaunchdRow>)list.ItemsSource!).Single().Cpu, Is.EqualTo("99.0%"));
        }
        finally { await view.Stop(); }
    }
    [AvaloniaTest]
    public void WorkerDialogRendersInBothLanguagesAtMinimumSize()
    {
        var context = Context(); var root = Process(42, 100); var job = new LaunchdJob(new(context, "example.job"), 42, 0, root.Identity);
        var tree = LaunchdWorkers.Build(job, Snapshot(context, job), Sample(root, Process(43, 200, 42)), true);
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        foreach (var language in new[] { "en", "es" })
        {
            UiText.Apply(language); var window = new JobWorkersWindow(job.Id.Label, DateTimeOffset.UnixEpoch, tree, true) { Width = 620, Height = 400 };
            try
            {
                window.Show(); Dispatcher.UIThread.RunJobs();
                var close = window.FindControl<Button>("CloseWorkers")!;
                Assert.That(close.TranslatePoint(new(close.Bounds.Width, close.Bounds.Height), window)!.Value.Y, Is.LessThanOrEqualTo(400));
                var items = window.FindControl<TreeView>("WorkerTree")!.GetVisualDescendants().OfType<TreeViewItem>().ToArray();
                Assert.That(items.Length, Is.EqualTo(2));
                foreach (var (headerName, reading) in new[] { ("WorkerCpuHeader", tree.Root!.Cpu), ("WorkerMemoryHeader", tree.Root.Memory), ("WorkerIoHeader", tree.Root.Io) })
                {
                    var header = window.FindControl<TextBlock>(headerName)!;
                    var cell = window.GetVisualDescendants().OfType<TextBlock>().Single(t => ReferenceEquals(t.DataContext, tree.Root) && t.Text == reading);
                    Assert.That(header.TranslatePoint(new(header.Bounds.Width, 0), window)!.Value.X,
                        Is.EqualTo(cell.TranslatePoint(new(cell.Bounds.Width, 0), window)!.Value.X).Within(1), headerName);
                }
                using var frame = window.CaptureRenderedFrame(); Assert.That(frame, Is.Not.Null);
                if (output is not null) frame!.Save(Path.Combine(output, "workers-" + language + ".png"), PngBitmapEncoderOptions.Default);
            }
            finally { window.Close(); UiText.Apply("en"); }
        }
    }
}
