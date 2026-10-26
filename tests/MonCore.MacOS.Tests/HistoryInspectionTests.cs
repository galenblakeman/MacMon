using System.Diagnostics;
using MacMon;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class HistoryInspectionTests
{
    private static MonitorSample Sample(int seconds) => new(DateTimeOffset.UnixEpoch.AddSeconds(seconds),
        10, [], 0, MonotonicTimestamp: (seconds + 1) * Stopwatch.Frequency);

    [Test]
    public void CaptureStaysAtSelectedMomentWhileLiveHistoryAdvances()
    {
        var samples = new[] { Sample(0), Sample(1), Sample(2) };
        var inspection = new HistoryInspection();
        inspection.Select(samples[1]);
        Assert.That(inspection.DisplayHistory(samples, samples[2]).Last(), Is.SameAs(samples[1]));
        Assert.That(inspection.DisplayHistory([.. samples, Sample(3)], Sample(3)), Has.Count.EqualTo(2));
        inspection.Live();
        var full = samples[2] with { CompactedProcesses = 0 };
        Assert.That(inspection.DisplayHistory(samples, full).Last(), Is.SameAs(full));
    }

    [Test]
    public void ExpiredCaptureDoesNotSilentlyBecomeLive()
    {
        var inspection = new HistoryInspection();
        inspection.Select(Sample(0));
        Assert.That(inspection.DisplayHistory([Sample(10)], Sample(10)), Is.Empty);
        Assert.That(inspection.IsHistorical, Is.True);
    }

    [Test]
    public void ChartSelectionUsesVisibleMonotonicTimeAndClampsToRecordedData()
    {
        var samples = new[] { Sample(0), Sample(100), Sample(150), Sample(200) };
        Assert.That(HistoryInspection.Nearest(samples, 120, 0), Is.SameAs(samples[1]));
        Assert.That(HistoryInspection.Nearest(samples, 120, 1), Is.SameAs(samples[3]));
        Assert.That(HistoryInspection.Nearest(samples, 120, 0.6), Is.SameAs(samples[2]));
        Assert.That(HistoryInspection.Nearest(samples, 120, double.NaN), Is.Null);
    }

    [Test]
    public void PortableExportPreservesUnknownCpuAndLifetimeDomain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"macmon-export-{Guid.NewGuid():N}.csv");
        var sample = Sample(1) with
        {
            IndependentProcessReadings = true,
            Processes = [new ProcessUsage(12, 345, "worker", 0) { CpuAvailable = false,
                LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds }]
        };
        try
        {
            CsvExport.Write(path, CsvExportKind.ProcessSamples, [sample], [], DateTimeOffset.UnixEpoch, 0, 0);
            var lines = File.ReadAllLines(path);
            var headers = lines[0].Split(',');
            var values = lines[1].Split(',');
            Assert.Multiple(() =>
            {
                Assert.That(values[Array.IndexOf(headers, "cpu_percent")], Is.Empty);
                Assert.That(values[Array.IndexOf(headers, "lifetime_domain")], Is.EqualTo("UnixMicroseconds"));
                Assert.That(values[Array.IndexOf(headers, "creation_ticks")], Is.EqualTo("345"));
            });
        }
        finally { File.Delete(path); }
    }
}
