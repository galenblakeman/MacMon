using System.IO.Compression;
using MacMon;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class MacHistoryTests
{
    private string _directory = null!;
    private string FilePath => Path.Combine(_directory, "history.mmh.gz");
    [SetUp] public void SetUp() => _directory = Directory.CreateTempSubdirectory("macmon-history-").FullName;
    [TearDown] public void TearDown() => Directory.Delete(_directory, true);

    private static MonitorSample Sample(int second, long moment) => new(
        new DateTimeOffset(2026, 10, 25, 20, 0, second, TimeSpan.FromHours(-5)), second == 0 ? null : 10,
        [new ProcessUsage(123, 100_000_456, "worker", 0, 1024, null)
        {
            LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, CpuAvailable = false,
            StartedAtUtc = DateTimeOffset.FromUnixTimeSeconds(100).AddTicks(4560),
            ImagePath = "/Applications/App", ParentProcessId = 1, ThreadCount = 5
        }], 2, second == 0, moment)
    {
        IndependentProcessReadings = true, CompactedProcesses = 100, IntervalSeconds = second == 0 ? 0 : 1,
        IntervalCpuTicks = second == 0 ? 0 : 8 * TimeSpan.TicksPerSecond,
        Resources = new ResourceSample
        {
            TotalMemoryBytes = 10000, UsedMemoryBytes = 6000, AvailableMemoryBytes = 4000, CachedBytes = 1000,
            CpuKernelPercent = second == 0 ? null : 5, ProcessCount = 103, DiskName = "/data", DiskFreeBytes = 1000, DiskTotalBytes = 2000,
            NetworkId = "en1", NetworkName = "Wi-Fi", ReceiveBytesPerSecond = second == 0 ? null : 0, SendBytesPerSecond = second == 0 ? null : 125000,
            SecondaryDiskName = "All block-storage drivers", SecondaryDiskReadBytesPerSecond = 4096, SecondaryDiskWriteBytesPerSecond = 0
        }
    };

    [Test]
    public void RoundTripPreservesPortableIdentityMissingReadingsAndResourceSemantics()
    {
        var original = Sample(1, 1000);
        MacHistoryStore.Save(FilePath, [new(original, "boot-a")], 1000);
        var loaded = MacHistoryStore.Load(FilePath, 120, original.Timestamp.AddSeconds(1), 5000, 1000);
        var actual = loaded.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Error, Is.Null);
            Assert.That(actual.BootId, Is.EqualTo("boot-a"));
            Assert.That(actual.Sample.Resources, Is.EqualTo(original.Resources));
            Assert.That(actual.Sample.Processes.Single(), Is.EqualTo(original.Processes.Single()));
            Assert.That(actual.Sample.Processes.Single().ObservedCpuPercent, Is.Null);
            Assert.That(actual.Sample.MonotonicTimestamp, Is.EqualTo(4000));
            Assert.That(actual.Sample.Timestamp.Offset, Is.EqualTo(TimeSpan.FromHours(-5)));
            Assert.That(loaded.RebasedKeys![SampleKey.Of(original)], Is.EqualTo(SampleKey.Of(actual.Sample)));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void RebootWithShortUptimeUsesOnePositiveVirtualClockAndKeepsBootProvenance()
    {
        var first = Sample(0, 100_000); var last = Sample(1, 101_000);
        MacHistoryStore.Save(FilePath, [new(first, "old-boot"), new(last, "old-boot")], 1000);
        var loaded = MacHistoryStore.Load(FilePath, 120, last.Timestamp.AddSeconds(20), 1000, 1000);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.ClockOffset, Is.GreaterThan(0));
            Assert.That(loaded.Entries[0].Sample.MonotonicTimestamp, Is.GreaterThan(0));
            Assert.That(loaded.Entries[1].Sample.MonotonicTimestamp - loaded.Entries[0].Sample.MonotonicTimestamp, Is.EqualTo(1000));
            Assert.That((1000 + loaded.ClockOffset - loaded.Entries[1].Sample.MonotonicTimestamp) / 1000d, Is.EqualTo(20));
            Assert.That(loaded.Entries.Select(entry => entry.BootId), Is.All.EqualTo("old-boot"));
        });
    }

    [Test]
    public void RetentionUsesSavedMonotonicIntervalsDespiteWallClockChangeWithinRun()
    {
        var first = Sample(0, 100_000) with { Timestamp = Sample(0, 1).Timestamp.AddHours(-1) };
        var last = Sample(1, 101_000);
        MacHistoryStore.Save(FilePath, [new(first, "a"), new(last, "b")], 1000);
        var loaded = MacHistoryStore.Load(FilePath, 120, last.Timestamp.AddSeconds(1), 10000, 1000);
        Assert.That(loaded.Entries, Has.Count.EqualTo(2));
        Assert.That(MacHistoryStore.Load(FilePath, 120, last.Timestamp.AddSeconds(121), 10000, 1000).Entries, Is.Empty);
        Assert.That(MacHistoryStore.Load(FilePath, 120, last.Timestamp.AddMinutes(-10), 10000, 1000).Error, Does.Contain("ahead"));
    }

    [Test]
    public void ProvenanceSurvivesRepeatedRestartsAndRejectsForeignIndividualPins()
    {
        var old = Sample(1, 1000);
        var current = Sample(2, 2000);
        var provenance = new HistoryProvenance("new-boot");
        provenance.Restore([new(old, "old-boot")]);
        Assert.That(provenance.CanPin(old, false, true), Is.False);
        Assert.That(provenance.CanPin(old, true, true), Is.True);
        Assert.That(provenance.CanPin(current, false, true), Is.True);
        Assert.That(provenance.SameBoot(old, current), Is.False);
        var snapshot = provenance.Snapshot([old, current]);
        MacHistoryStore.Save(FilePath, snapshot, 1000);
        var loaded = MacHistoryStore.Load(FilePath, 120, current.Timestamp.AddSeconds(1), 10000, 1000);
        var next = new HistoryProvenance("third-boot");
        next.Restore(loaded.Entries);
        Assert.That(next.Snapshot(loaded.Entries.Select(entry => entry.Sample).ToArray()).Select(entry => entry.BootId),
            Is.EqualTo(new[] { "old-boot", "new-boot" }));
    }

    [Test]
    public void CapturePeakSurvivesRepeatedRebasingButAmbiguousWallTimesDoNotMatch()
    {
        var original = Sample(1, 1000);
        MacHistoryStore.Save(FilePath, [new(original, "boot")], 1000);
        var first = MacHistoryStore.Load(FilePath, 120, original.Timestamp.AddSeconds(1), 10000, 1000);
        MacHistoryStore.Save(FilePath, first.Entries, 1000);
        var second = MacHistoryStore.Load(FilePath, 120, original.Timestamp.AddSeconds(2), 20000, 1000);
        Assert.That(second.ResolvePeak(SampleKey.Of(original)), Is.EqualTo(SampleKey.Of(second.Entries.Single().Sample)));
        var ambiguous = new HistoryLoadResult([new(original with { MonotonicTimestamp = 5000 }, "a"),
            new(original with { MonotonicTimestamp = 6000 }, "b")]);
        Assert.That(ambiguous.ResolvePeak(SampleKey.Of(original)).MonotonicTimestamp, Is.Zero);
    }

    [Test]
    public void VisibleRangeFollowsMonotonicChartCoordinatesWhenWallTimeJumps()
    {
        var first = Sample(1, 1000) with { Timestamp = Sample(1, 1000).Timestamp.AddHours(1) };
        var last = Sample(2, 2000);
        Assert.That(HistoryInspection.VisibleSamples([first, last], 2), Has.Count.EqualTo(2));
    }

    [Test]
    public void FailedSaveCannotReplaceACompleteFile()
    {
        var sample = Sample(1, 1000);
        MacHistoryStore.Save(FilePath, [new(sample, "boot")], 1000);
        var before = File.ReadAllBytes(FilePath);
        var windows = sample with { Processes = [new ProcessUsage(1, 123, "Windows", 1)] };
        Assert.Throws<InvalidDataException>(() => MacHistoryStore.Save(FilePath, [new(windows, "boot")], 1000));
        Assert.That(File.ReadAllBytes(FilePath), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void CorruptCompressedPayloadAndOversizedCountsArePreserved()
    {
        File.WriteAllBytes(FilePath, [1, 2, 3]);
        var corrupt = MacHistoryStore.Load(FilePath, 120, DateTimeOffset.UtcNow, 1000, 1000);
        Assert.That(corrupt.Entries, Is.Empty);
        Assert.That(Directory.GetFiles(_directory, "*.invalid-*"), Has.Length.EqualTo(1));
        using (var file = File.Create(FilePath))
        using (var zip = new GZipStream(file, CompressionLevel.Fastest))
        using (var writer = new BinaryWriter(zip))
        { writer.Write(0x31484D4D); writer.Write(1); writer.Write(1000L); writer.Write(int.MaxValue); }
        Assert.That(MacHistoryStore.Load(FilePath, 120, DateTimeOffset.UtcNow, 1000, 1000).Error, Does.Contain("preserved"));
        Assert.That(Directory.GetFiles(_directory, "*.invalid-*"), Has.Length.EqualTo(2));
    }

    [Test]
    public void UnknownVersionRemainsUntouchedAndDisablesSaves()
    {
        using (var file = File.Create(FilePath))
        using (var zip = new GZipStream(file, CompressionLevel.Fastest))
        using (var writer = new BinaryWriter(zip))
        { writer.Write(0x31484D4D); writer.Write(99); }
        var original = File.ReadAllBytes(FilePath);
        var loaded = MacHistoryStore.Load(FilePath, 120, DateTimeOffset.UtcNow, 1000, 1000);
        Assert.That(loaded.CanSave, Is.False);
        Assert.That(File.ReadAllBytes(FilePath), Is.EqualTo(original));
    }

    [Test]
    public void TruncatedPayloadDoesNotExposePartiallyReadSamples()
    {
        var sample = Sample(1, 1000);
        MacHistoryStore.Save(FilePath, [new(sample, "boot")], 1000);
        var bytes = File.ReadAllBytes(FilePath);
        File.WriteAllBytes(FilePath, bytes[..(bytes.Length / 2)]);
        Assert.That(MacHistoryStore.Load(FilePath, 120, sample.Timestamp.AddSeconds(1), 10000, 1000).Entries, Is.Empty);
    }
}
