using System.Diagnostics;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class ProcessPresentationTests
{
    private static ProcessUsage Process(int pid, string path, double cpu, ulong memory = 100) =>
        new(pid, pid * 100, "worker", cpu, memory)
        { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = path, ThreadCount = 2 };

    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, processes, 0,
            MonotonicTimestamp: (second + 1) * Stopwatch.Frequency) { IndependentProcessReadings = true };

    [Test]
    public void GroupingUsesExactPathsAndSortsByResidentSum()
    {
        var sample = Sample(1, Process(1, "/A", 2, 100), Process(2, "/A", 3, 200), Process(3, "/a", 1, 250));
        var rows = ProcessPresentation.Rows([sample], sample, true, ProcessSort.Memory, "",
            new(MacFrameSource.IdentityPolicy), false);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0].Process.ImagePath, Is.EqualTo("/A"));
            Assert.That(rows[0].Process.MemberCount, Is.EqualTo(2));
            Assert.That(rows[0].Process.WorkingSetBytes, Is.EqualTo(300));
            Assert.That(rows[0].Process.ObservedCpuPercent, Is.EqualTo(5));
            Assert.That(rows[0].Path, Does.Contain("shared pages"));
        });
    }

    [Test]
    public void GroupSearchMatchesMemberPidAndKeepsWholeGroup()
    {
        var sample = Sample(1, Process(11, "/A", 2), Process(22, "/A", 3));
        var rows = ProcessPresentation.Rows([sample], sample, true, ProcessSort.Cpu, "22",
            new(MacFrameSource.IdentityPolicy), false);
        Assert.That(rows.Single().Process.MemberCount, Is.EqualTo(2));
    }

    [Test]
    public void IndividualPinCannotFollowReusedPidAndIsNotDuplicated()
    {
        var original = Process(1, "/A", 2);
        var pins = new ProcessPins(MacFrameSource.IdentityPolicy);
        pins.Toggle(original, false);
        var first = Sample(1, original);
        Assert.That(ProcessPresentation.Rows([first], first, false, ProcessSort.Cpu, "", pins, false), Has.Length.EqualTo(1));
        var latest = Sample(2, original with { CreationTicks = 999, CpuPercent = 10 });
        var rows = ProcessPresentation.Rows([first, latest], latest, false, ProcessSort.Cpu, "", pins, false);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0].Pinned, Is.True);
            Assert.That(rows[0].State, Is.EqualTo(PinnedProcessState.Exited));
            Assert.That(rows[0].Cpu, Is.EqualTo("—"));
            Assert.That(rows[1].Process.CreationTicks, Is.EqualTo(999));
        });
    }

    [Test]
    public void GroupPinFollowsNewLifetimeAndMissingTrailValuesStayMissing()
    {
        var original = Process(1, "/A", 2);
        var pins = new ProcessPins(MacFrameSource.IdentityPolicy);
        pins.Toggle(original, true);
        var latest = Sample(3, Process(2, "/A", 4));
        var rows = ProcessPresentation.Rows([Sample(1, original), Sample(2), latest], latest,
            true, ProcessSort.Cpu, "", pins, true);
        Assert.Multiple(() =>
        {
            Assert.That(rows.Single().State, Is.EqualTo(PinnedProcessState.Present));
            Assert.That(rows[0].Trail.Values, Is.EqualTo(new double?[] { 2, null, 4 }));
        });
    }

    [Test]
    public void TrailBreaksAcrossWakeGapAndUnavailableGroupMember()
    {
        var first = Sample(1, Process(1, "/A", 2));
        var last = Sample(10, Process(1, "/A", 4), Process(2, "/A", 0) with { CpuAvailable = false });
        var row = ProcessPresentation.Rows([first, last], last, true, ProcessSort.Cpu, "",
            new(MacFrameSource.IdentityPolicy), false).Single();
        Assert.That(row.Trail.Values, Is.EqualTo(new double?[] { 2, null, null }));
    }
}
