using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class LaunchdPresentationTests
{
    private static readonly LaunchdContext Context = new(Guid.NewGuid(), 501, "Aqua");
    private static readonly ProcessUsage Process = new(42, 123456, "worker", 1)
    { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = "/Applications/Worker" };
    private static readonly LaunchdJob Running = new(new(Context, "com.example.worker"), 42, -9, Process.Identity);
    private static readonly LaunchdJob Idle = new(new(Context, "com.example.idle"), null, 0);
    private static LaunchdSnapshot Snapshot => new(DateTimeOffset.Now, Context, [Running, Idle]);
    private static MonitorSample Frame(ProcessUsage process) => new(DateTimeOffset.Now, 10, [process], 0);

    [Test]
    public void FilterSortAndSearchDoNotConfuseIdleWithDisabledOrExitWithHealth()
    {
        var rows = LaunchdPresentation.Rows(Snapshot, Frame(Process), true, null, 0, 0);
        Assert.That(rows.First().Label, Is.EqualTo(Idle.Id.Label));
        Assert.That(rows.First().State, Is.EqualTo("Not running"));
        Assert.That(rows.Last().State, Is.EqualTo("Running"));
        Assert.That(LaunchdPresentation.Rows(Snapshot, Frame(Process), true, "WORKER", 1, 1).Single().Job, Is.EqualTo(Running));
        Assert.That(LaunchdPresentation.Rows(Snapshot, Frame(Process), true, "42", 0, 2).Single().ExitStatus, Is.EqualTo("Signal 9"));
        Assert.That(LaunchdPresentation.Rows(Snapshot, null, false, "", 2, 0).Single().Job, Is.EqualTo(Idle));
        Assert.That(LaunchdPresentation.ExitText(int.MinValue), Is.EqualTo("Signal 2147483648"));
    }

    [Test]
    public void ProcessDetailsRequireFreshExactLifetimeAndNeverUseHistoricalPidOnlyRows()
    {
        Assert.That(LaunchdPresentation.Match(Running, Frame(Process), true), Is.EqualTo(Process));
        Assert.That(LaunchdPresentation.Match(Running, Frame(Process with { CreationTicks = 987654 }), true), Is.Null);
        Assert.That(LaunchdPresentation.Match(Running, Frame(Process), false), Is.Null);
        Assert.That(LaunchdPresentation.Match(Running with { ObservedIdentity = null }, Frame(Process), true), Is.Null);
        var text = LaunchdPresentation.Details(Running, Snapshot.ObservedAt, Frame(Process), true, false);
        Assert.That(text, Does.Contain("/Applications/Worker"));
        Assert.That(text, Does.Contain("not current health"));
        var stale = LaunchdPresentation.Details(Running, Snapshot.ObservedAt, Frame(Process), true, true);
        Assert.That(stale, Does.Contain("STALE"));
        Assert.That(stale, Does.Not.Contain("/Applications/Worker"));
    }

    [Test]
    public void ResourceRankingAndTrailsRequireFreshVerifiedDirectProcessLifetimes()
    {
        var first = Frame(Process with { WorkingSetBytes = 1048576, IoBytesPerSecond = 2097152 }) with { MonotonicTimestamp = System.Diagnostics.Stopwatch.Frequency };
        var second = first with { MonotonicTimestamp = 2 * System.Diagnostics.Stopwatch.Frequency, Processes = [first.Processes[0] with { CpuPercent = 4 }] };
        var gap = second with { MonotonicTimestamp = 8 * System.Diagnostics.Stopwatch.Frequency };
        var rows = LaunchdPresentation.Rows(Snapshot, gap, true, null, 0, 3, 5, [first, second, gap]);
        Assert.That(rows[0].Job, Is.EqualTo(Running));
        var app = Running with { Id = new(Context, "application.example.worker.123") };
        var friendly = LaunchdPresentation.Rows(new(Snapshot.ObservedAt, Context, [app]), gap, true, null, 0, 0).Single();
        Assert.That(friendly.Name, Is.EqualTo("worker"));
        Assert.That(friendly.ProcessDescription, Does.Contain(app.Id.Label));
        Assert.That(rows[0].Cpu, Is.EqualTo("4.0%"));
        Assert.That(rows[0].Resident, Is.EqualTo("1.0 MiB"));
        Assert.That(rows[0].Io, Is.EqualTo("2.1 MB/s"));
        Assert.That(rows[0].Trail.Values, Is.EqualTo(new double?[] { 1, 4, null, 4 }));
        foreach (var fresh in new[] { true, false })
        {
            var reused = gap with { Processes = [Process with { CreationTicks = 999999 }] };
            var row = LaunchdPresentation.Rows(Snapshot, reused, fresh, "worker", 0, 4, 5, [first, reused]).Single();
            Assert.That(row.Cpu, Is.EqualTo("—"));
            Assert.That(row.Trail.Values, Is.Empty);
        }
        Assert.That(LaunchdPresentation.Rows(Snapshot, first, false, "worker", 0, 5, history: [first]).Single().Io, Is.EqualTo("—"));
        Assert.That(LaunchdPresentation.Rows(Snapshot, first, true, "idle", 2, 3, 5), Has.Count.EqualTo(1));
    }

    [Test]
    public void VisibleJobTrailsPreserveSharedLifetimesMissingReadingsAndMemoryAcrossGaps()
    {
        var frequency = System.Diagnostics.Stopwatch.Frequency;
        var sibling = Running with { Id = new(Context, "com.example.sibling") };
        var snapshot = Snapshot with { Jobs = [Running, sibling, Idle, .. Enumerable.Range(0, 10)
            .Select(index => Idle with { Id = new(Context, $"com.example.waiting{index}") })] };
        var first = Frame(Process with { WorkingSetBytes = 100 }) with { MonotonicTimestamp = frequency };
        // The same PID with a different lifetime must leave a hole in both jobs' trails.
        var reused = Frame(Process with { CreationTicks = 999, WorkingSetBytes = 9000 }) with { MonotonicTimestamp = 2 * frequency };
        var gap = Frame(Process with { WorkingSetBytes = 200, CpuAvailable = false }) with { MonotonicTimestamp = 8 * frequency, IsGap = true };
        var latest = Frame(Process with { WorkingSetBytes = 300, CpuPercent = 4 }) with { MonotonicTimestamp = 9 * frequency };
        latest = latest with { Processes = [.. latest.Processes, .. Enumerable.Range(100, 12)
            .Select(pid => Process with { ProcessId = pid, WorkingSetBytes = 9000 })] };
        MonitorSample[] history = [first, reused, gap, latest];
        var memory = LaunchdPresentation.Rows(snapshot, latest, true, null, 0, 4, history: history);
        foreach (var row in memory.Where(row => row.Process is not null))
        {
            Assert.That(row.Trail.Values, Is.EqualTo(new double?[] { 100, null, null, 200, 300 }));
            Assert.That(row.Trail.Maximum, Is.EqualTo(300));
        }
        Assert.That(memory.Single(row => row.Job == Idle).Trail.Values, Is.Empty);
        var cpu = LaunchdPresentation.Rows(snapshot, latest, true, null, 0, 3, 5, history);
        Assert.That(cpu, Has.Count.EqualTo(5));
        Assert.That(cpu[0].Trail.Values, Is.EqualTo(new double?[] { 1, null, null, null, 4 }));
        Assert.That(cpu[0].Trail.Maximum, Is.EqualTo(100));
        Assert.That(LaunchdPresentation.Rows(snapshot, latest, true, "no match", 0, 4, history: history), Is.Empty);
        var idle = LaunchdPresentation.Rows(snapshot, latest, true, "com.example.idle", 2, 4, history: history).Single();
        Assert.That(idle.Trail.Values, Is.Empty);
        Assert.That(idle.Trail.Maximum, Is.EqualTo(1));
    }

    [Test]
    public void DiscoveryFailurePreservesLastSuccessWithExplicitErrorAndRecoveryReplacesRows()
    {
        var inventory = new LaunchdInventory();
        inventory.Apply(Snapshot);
        var previous = inventory.LastSuccess;
        inventory.Apply(new(DateTimeOffset.Now, null, [], "Denied"));
        Assert.That(inventory.LastSuccess, Is.SameAs(previous));
        Assert.That(inventory.Error, Is.EqualTo("Denied"));
        inventory.Apply(new(DateTimeOffset.Now, Context, []));
        Assert.That(inventory.LastSuccess!.Jobs, Is.Empty);
        Assert.That(inventory.Error, Is.Null);
    }
}
