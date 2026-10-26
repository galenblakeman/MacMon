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
        Assert.That(rows[0].Io, Is.EqualTo("2.00 MiB/s"));
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
