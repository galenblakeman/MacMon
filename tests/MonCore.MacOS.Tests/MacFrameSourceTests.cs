using System.Diagnostics;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

[TestFixture]
public sealed class MacFrameSourceTests
{
    private static readonly SamplingOptions Options = new();

    [Test]
    public void FirstReadingRetainsCapacityWithoutInventingCpuOrMemoryUsage()
    {
        using var source = Source(Observation(0, new(10, 10, 80, 0)));
        var sample = source.Capture(Options).Full;
        Assert.Multiple(() =>
        {
            Assert.That(sample.TotalCpuPercent, Is.Null);
            Assert.That(sample.IsGap, Is.True);
            Assert.That(sample.Resources.TotalMemoryBytes, Is.EqualTo(16UL << 30));
            Assert.That(sample.Resources.UsedMemoryBytes, Is.Null);
            Assert.That(sample.Resources.AvailableMemoryBytes, Is.Null);
            Assert.That(sample.Resources.CommittedBytes, Is.Null);
            Assert.That(sample.Resources.DiskFreeBytes, Is.EqualTo(100UL << 30));
            Assert.That(sample.Processes, Is.Empty);
        });
    }

    [Test]
    public void CpuUsesWholeMachineCapacityAndIncludesNiceTime()
    {
        using var source = Source(Observation(0, new(10, 20, 60, 10)),
            Observation(1, new(30, 30, 125, 15)));
        source.Capture(Options);
        var sample = source.Capture(Options).Full;
        Assert.Multiple(() =>
        {
            Assert.That(sample.TotalCpuPercent, Is.EqualTo(35));
            Assert.That(sample.Resources.CpuKernelPercent, Is.EqualTo(10));
            Assert.That(sample.IntervalSeconds, Is.EqualTo(1));
            Assert.That(sample.IntervalCpuTicks, Is.EqualTo(8 * TimeSpan.TicksPerSecond));
        });
    }

    [Test]
    public void SleepGapRequiresFreshBaselineAndCannotCreateCpuSpike()
    {
        using var source = Source(Observation(0, new(0, 0, 0, 0)),
            Observation(20, new(30, 20, 150, 0)), Observation(21, new(40, 30, 230, 0)));
        source.Capture(Options);
        Assert.That(source.Capture(Options).Full.IsGap, Is.True);
        Assert.That(source.Capture(Options).Full.TotalCpuPercent, Is.EqualTo(20));
    }

    [Test]
    public void SleepIsAGapEvenWhenMonotonicClockStopsDuringSleep()
    {
        var before = Observation(0, new(0, 0, 0, 0));
        var after = Observation(1, new(10, 10, 80, 0)) with
        {
            Timestamp = before.Timestamp.AddHours(1)
        };
        using var source = Source(before, after);
        source.Capture(Options);
        Assert.That(source.Capture(Options).Full.IsGap, Is.True);
    }

    [Test]
    public void FailedCpuReadPreservesResourcesAndBreaksNextInterval()
    {
        using var source = Source(Observation(0, new(0, 0, 0, 0)), Observation(1, null),
            Observation(2, new(10, 10, 80, 0)), Observation(3, new(20, 20, 160, 0)));
        source.Capture(Options);
        var failed = source.Capture(Options);
        Assert.That(failed.Error, Is.EqualTo(SamplingError.CpuUnavailable));
        Assert.That(failed.Full.Resources.TotalMemoryBytes, Is.Not.Null);
        Assert.That(source.Capture(Options).Full.TotalCpuPercent, Is.Null);
        Assert.That(source.Capture(Options).Full.TotalCpuPercent, Is.EqualTo(20));
    }

    [Test]
    public void CounterRolloverDoesNotProduceNegativeCpu()
    {
        using var source = Source(Observation(0, new(uint.MaxValue - 4, 10, 10, 0)),
            Observation(1, new(5, 20, 90, 0)));
        source.Capture(Options);
        Assert.That(source.Capture(Options).Full.TotalCpuPercent, Is.EqualTo(20));
    }

    [Test]
    public void ResetAndZeroDeltaDoNotInventIdleReadings()
    {
        var reading = new CpuCounters(1, 1, 8, 0);
        using var source = Source(Observation(0, reading), Observation(1, reading),
            Observation(2, new(10, 10, 80, 0)));
        source.Capture(Options);
        Assert.That(source.Capture(Options).Full.TotalCpuPercent, Is.Null);
        source.Reset();
        Assert.That(source.Capture(Options).Full.TotalCpuPercent, Is.Null);
    }

    [Test]
    public void DisposeReleasesReaderOnceAndPreventsCapture()
    {
        var reader = new FakeReader([]);
        var source = new MacFrameSource(reader);
        source.Dispose();
        source.Dispose();
        Assert.That(reader.DisposeCount, Is.EqualTo(1));
        Assert.Throws<ObjectDisposedException>(() => source.Capture(Options));
    }

    [Test]
    public void NativeMacCollectorReadsRealCountersAndReleasesPort()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("Native macOS integration test.");
        using var source = new MacFrameSource();
        var frame = source.Capture(Options);
        Assert.Multiple(() =>
        {
            Assert.That(frame.Error, Is.Null);
            Assert.That(frame.Full.Resources.TotalMemoryBytes, Is.GreaterThan(0));
            Assert.That(frame.Full.Resources.DiskFreeBytes, Is.GreaterThan(0));
        });
    }

    private static MacFrameSource Source(params SystemObservation[] observations) => new(new FakeReader(observations));

    private static SystemObservation Observation(int seconds, CpuCounters? cpu) => new(
        DateTimeOffset.UnixEpoch.AddSeconds(seconds), (seconds + 1) * Stopwatch.Frequency,
        cpu, 8, 16UL << 30, "/System/Volumes/Data", 100UL << 30, 500UL << 30);

    private sealed class FakeReader(IEnumerable<SystemObservation> readings) : IMacSystemReader
    {
        private readonly Queue<SystemObservation> _readings = new(readings);
        public int DisposeCount { get; private set; }
        public SystemObservation Read(string volumePath, string? networkId = null) => _readings.Dequeue();
        public void Dispose() => DisposeCount++;
    }
}
