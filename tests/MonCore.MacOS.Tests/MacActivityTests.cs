using System.Diagnostics;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class MacActivityTests
{
    private static SystemObservation Observation(int second, string network = "en0", string storage = "1", ulong? count = 100) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), (second + 1) * Stopwatch.Frequency,
            new(1, 1, (uint)second + 1, 0), 8, 10000, "/data", 1000, 2000)
        { Activity = new(network, network, count, count, storage, count, count, []) };

    [Test]
    public void RatesUseActualIntervalAndDoNotMixVolumeCapacityWithStorageActivity()
    {
        using var source = new MacFrameSource(new Reader([Observation(0), Observation(2, count: 300)]));
        Assert.That(source.Capture(new()).Full.Resources.ReceiveBytesPerSecond, Is.Null);
        var resources = source.Capture(new()).Full.Resources;
        Assert.Multiple(() =>
        {
            Assert.That(resources.ReceiveBytesPerSecond, Is.EqualTo(100));
            Assert.That(resources.DiskReadBytesPerSecond, Is.EqualTo(100));
            Assert.That(resources.DiskName, Is.EqualTo("All block-storage drivers"));
            Assert.That(resources.SecondaryDiskReadBytesPerSecond, Is.Null);
            Assert.That(resources.SecondaryDiskName, Is.Empty);
        });
    }

    [TestCase("en1", "1", 200UL, 1, true, false)]
    [TestCase("en0", "2", 200UL, 1, false, true)]
    [TestCase("en0", "1", 50UL, 1, true, true)]
    [TestCase("en0", "1", 200UL, 20, true, true)]
    public void SourceChangesRollbacksAndWakeGapsRequireFreshBaselines(string network, string storage,
        ulong count, int seconds, bool missingNetwork, bool missingDisk)
    {
        using var source = new MacFrameSource(new Reader([Observation(0), Observation(seconds, network, storage, count)]));
        source.Capture(new());
        var resource = source.Capture(new()).Full.Resources;
        Assert.That(resource.ReceiveBytesPerSecond is null, Is.EqualTo(missingNetwork));
        Assert.That(resource.DiskReadBytesPerSecond is null, Is.EqualTo(missingDisk));
    }

    [Test]
    public void FailedCountersNeedTwoSuccessfulObservationsToRecover()
    {
        using var source = new MacFrameSource(new Reader([Observation(0), Observation(1, count: null),
            Observation(2, count: 300), Observation(3, count: 400)]));
        source.Capture(new());
        Assert.That(source.Capture(new()).Full.Resources.ReceiveBytesPerSecond, Is.Null);
        Assert.That(source.Capture(new()).Full.Resources.ReceiveBytesPerSecond, Is.Null);
        Assert.That(source.Capture(new()).Full.Resources.ReceiveBytesPerSecond, Is.EqualTo(100));
    }

    [Test]
    public void NativeStorageCountersAndInterfaceChoicesAreReadable()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("macOS integration test");
        var activity = new MacActivityReader().Read(null);
        Assert.Multiple(() =>
        {
            Assert.That(activity.Read, Is.Not.Null);
            Assert.That(activity.Written, Is.Not.Null);
            Assert.That(activity.Interfaces.Select(choice => choice.Id).Distinct().Count(), Is.EqualTo(activity.Interfaces.Count));
        });
    }

    private sealed class Reader(IEnumerable<SystemObservation> observations) : IMacSystemReader
    {
        private readonly Queue<SystemObservation> _queue = new(observations);
        public SystemObservation Read(string volumePath, string? networkId = null) => _queue.Dequeue();
        public void Dispose() { }
    }
}
