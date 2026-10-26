using System.Globalization;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class TableUnitsTests
{
    [TestCase(0, "0.0 B", "0.0 B/s")]
    [TestCase(512, "512.0 B", "512.0 B/s")]
    [TestCase(1024, "1.0 KiB", "1.0 KiB/s")]
    [TestCase(1536, "1.5 KiB", "1.5 KiB/s")]
    [TestCase(1048576, "1.0 MiB", "1.0 MiB/s")]
    [TestCase(1073741824, "1.0 GiB", "1.0 GiB/s")]
    [TestCase(1099511627776, "1.0 TiB", "1.0 TiB/s")]
    public void SelectsUnitsFromEachValuesMagnitude(double value, string memory, string rate)
    {
        using var culture = new CultureScope("en-US");
        Assert.That(TableUnits.Memory(value), Is.EqualTo(memory));
        Assert.That(TableUnits.IoRate(value), Is.EqualTo(rate));
        Assert.That(TableUnits.IoTotal(value), Is.EqualTo(rate.Replace("/s", "")));
    }

    [Test]
    public void RoundingPromotesToTheNextUnitAndUnavailableStaysDistinctFromZero()
    {
        using var culture = new CultureScope("en-US");
        Assert.That(TableUnits.Memory(1048575), Is.EqualTo("1.0 MiB"));
        Assert.That(TableUnits.IoRate(1048575), Is.EqualTo("1.0 MiB/s"));
        foreach (var value in new double?[] { null, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.That(TableUnits.Memory(value), Is.EqualTo("—"));
            Assert.That(TableUnits.IoRate(value), Is.EqualTo("—"));
            Assert.That(TableUnits.IoTotal(value), Is.EqualTo("—"));
        }
        using var spanish = new CultureScope("es-ES");
        Assert.That(TableUnits.Memory(1536), Is.EqualTo("1,5 KiB"));
        Assert.That(TableUnits.IoRate(1536), Is.EqualTo("1,5 KiB/s"));
    }

    [Test]
    public void ProcessAndJobRowsUseTheSameAdaptiveUnitsAndKeepMissingReadingsUnavailable()
    {
        using var culture = new CultureScope("en-US");
        var process = new ProcessUsage(42, 123, "worker", 1) { WorkingSetBytes = 2147483648, IoBytesPerSecond = 512 };
        var row = new ProcessRow(process);
        var context = new LaunchdContext(Guid.Empty, 501, "Aqua");
        var job = new LaunchdJob(new(context, "example.worker"), 42, 0, process.Identity);
        var snapshot = new LaunchdSnapshot(DateTimeOffset.UnixEpoch, context, [job]);
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 10, [process], 0);
        var jobRow = LaunchdPresentation.Rows(snapshot, sample, true, "", 0, 0).Single();
        Assert.That(row.Resident, Is.EqualTo("2.0 GiB"));
        Assert.That(jobRow.Resident, Is.EqualTo(row.Resident));
        Assert.That(row.Io, Is.EqualTo("512.0 B/s"));
        Assert.That(jobRow.Io, Is.EqualTo(row.Io));
        var missing = new ProcessRow(process with { WorkingSetBytes = null, IoBytesPerSecond = null });
        Assert.That(missing.Resident, Is.EqualTo("—")); Assert.That(missing.Io, Is.EqualTo("—"));
        var stale = LaunchdPresentation.Rows(snapshot, sample, false, "", 0, 0).Single();
        Assert.That(stale.Resident, Is.EqualTo("—")); Assert.That(stale.Io, Is.EqualTo("—"));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
