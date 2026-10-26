using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class MacMemoryTests
{
    [TestCase(4096u)]
    [TestCase(16384u)]
    public void UsageExcludesPurgeableAndCountsCompressedStorageOnce(uint pageSize)
    {
        var snapshot = MacMemorySnapshot.FromPages(10000UL * pageSize, pageSize, 3000, 500, 1000, 200, 1500, "Normal");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.UsedBytes, Is.EqualTo(3700UL * pageSize));
            Assert.That(snapshot.CachedBytes, Is.EqualTo(2000UL * pageSize));
            Assert.That(snapshot.CompressedBytes, Is.EqualTo(200UL * pageSize));
        });
    }

    [Test]
    public void InvalidCountersRemainUnavailable()
    {
        Assert.That(MacMemorySnapshot.FromPages(null, 4096, 3, 0, 1, 0, 0, "Normal").UsedBytes, Is.Null);
        Assert.That(MacMemorySnapshot.FromPages(4096, 4096, 3, 0, 1, 0, 0, "Normal").UsedBytes, Is.Null);
        Assert.That(MacMemorySnapshot.FromPages(40960, 4096, 3, 4, 1, 0, 0, "Normal").UsedBytes, Is.Null);
    }

    [TestCase(1u, "Normal")]
    [TestCase(2u, "Warning")]
    [TestCase(4u, "Critical")]
    [TestCase(0u, "Unavailable")]
    [TestCase(99u, "Unavailable")]
    public void PressureUsesDispatchFlagsRatherThanInternalKernelLevels(uint flag, string expected) =>
        Assert.That(MacMemorySnapshot.PressureName(flag), Is.EqualTo(expected));

    [Test]
    public void NativeMemoryIsAvailableEvenOnCpuBaseline()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("macOS integration test");
        using var source = new MacFrameSource();
        var sample = source.Capture(new SamplingOptions()).Full;
        Assert.Multiple(() =>
        {
            Assert.That(sample.TotalCpuPercent, Is.Null);
            Assert.That(sample.Resources.MemoryPercent, Is.InRange(0d, 100d));
            Assert.That(source.Memory.CompressedBytes, Is.Not.Null);
            Assert.That(sample.Resources.UsedMemoryBytes + sample.Resources.AvailableMemoryBytes,
                Is.EqualTo(sample.Resources.TotalMemoryBytes));
        });
    }
}
