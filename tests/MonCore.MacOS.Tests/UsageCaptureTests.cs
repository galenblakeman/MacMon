using System.Diagnostics;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class UsageCaptureTests
{
    private static ProcessUsage Process(int pid, string path, double cpu = 10) => new(pid, pid * 100,
        "worker", cpu, 100, 1024) { ImagePath = path, LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 50, processes, 0,
            MonotonicTimestamp: (second + 1) * Stopwatch.Frequency)
        { IndependentProcessReadings = true, IntervalSeconds = 1, IntervalCpuTicks = 8 * TimeSpan.TicksPerSecond };

    [Test]
    public void UsageIncludesExitedAppsAndUsesExactExecutableKeys()
    {
        var ledger = new UsageLedger(DateTimeOffset.UnixEpoch, MacFrameSource.IdentityPolicy);
        ledger.Add(Sample(1, Process(1, "/A"), Process(2, "/A"), Process(3, "/a", 30)));
        ledger.Add(Sample(2));
        var rows = UsagePresentation.Rows(ledger, "", UsageSort.Cpu);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0].App.ImagePath, Is.EqualTo("/a"));
            Assert.That(rows[1].App.Lifetimes, Is.EqualTo(2));
            Assert.That(rows[1].App.Running, Is.Zero);
            Assert.That(rows[1].App.IoBytes, Is.EqualTo(2048));
            Assert.That(UsagePresentation.Rows(ledger, "/A", UsageSort.DiskIo), Has.Length.EqualTo(2));
        });
        ledger.Reset(DateTimeOffset.UtcNow);
        Assert.That(UsagePresentation.Rows(ledger, "", UsageSort.Cpu), Is.Empty);
    }

    [Test]
    public void OptionsRejectUnsupportedRulesAndNormalizeRanges()
    {
        var options = new MacCaptureOptions(true, [new(CaptureMetric.Cpu, true, double.NaN, 0),
            new(CaptureMetric.Gpu, true, 5, 1)], 0).Normalized();
        Assert.Multiple(() =>
        {
            Assert.That(options.Rules.Select(rule => rule.Metric), Is.EquivalentTo(MacCaptureOptions.SupportedMetrics));
            Assert.That(options.Rules.Single(rule => rule.Metric == CaptureMetric.Cpu).Threshold, Is.EqualTo(90));
            Assert.That(options.Rules.Single(rule => rule.Metric == CaptureMetric.Cpu).Seconds, Is.EqualTo(1));
            Assert.That(options.CooldownSeconds, Is.EqualTo(1));
        });
    }

    [Test]
    public void CaptureBreaksOnMissingNetworkAndPreservesPeakAfterRearm()
    {
        var rules = new CaptureRules([new(CaptureMetric.Network, true, 1, 2)], TimeSpan.FromSeconds(1), 8);
        MonitorSample Network(int second, double? rx) => Sample(second) with
        { Resources = new ResourceSample { ReceiveBytesPerSecond = rx, SendBytesPerSecond = 0 } };
        Assert.That(rules.Observe(Network(1, 1_000_000)), Is.Empty);
        Assert.That(rules.Observe(Network(2, null)), Is.Empty);
        Assert.That(rules.Observe(Network(3, 2_000_000)), Is.Empty);
        var fired = rules.Observe(Network(4, 1_000_000)).Single();
        Assert.That(fired.Peak.Timestamp, Is.EqualTo(Network(3, 0).Timestamp));
        Assert.That(rules.Observe(Network(5, 2_000_000)), Is.Empty);
        rules.Observe(Network(6, 0)); rules.Observe(Network(7, 0));
        rules.Observe(Network(8, 1_000_000));
        Assert.That(rules.Observe(Network(9, 1_000_000)), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReportQueueDrainsAndPrunesOnlyOwnedReports()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"macmon-captures-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var foreign = Path.Combine(directory, $"MacMon-capture-foreign-{Guid.NewGuid():N}.txt");
        var winmon = Path.Combine(directory, "WinMon-capture.txt");
        File.WriteAllText(foreign, "User report"); File.WriteAllText(winmon, "WinMon report");
        try
        {
            var rule = new CaptureRuleSetting(CaptureMetric.Cpu, true, 1, 1);
            var first = Sample(1, Process(1, "/A"));
            var fired = new CaptureFired(rule, first, first, 50, [50]);
            var record = MacCaptureReports.Create(fired, directory);
            var text = MacCaptureReports.AutomaticText(fired);
            var completed = new List<string?>();
            var writer = new CaptureReportWriter((_, error) => completed.Add(error));
            Assert.That(writer.Enqueue(record, text), Is.True);
            await writer.Complete();
            Assert.That(completed, Is.EqualTo(new string?[] { null }));
            Assert.That(File.ReadAllText(record.ReportPath), Does.Contain("disk I/O excludes network traffic"));
            Assert.That(text, Does.Contain("without five-second averaging"));
            var second = MacCaptureReports.Create(fired with { Peak = Sample(2) }, directory);
            MacCaptureReports.Write(second, text, retain: 1);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(record.ReportPath), Is.False);
                Assert.That(File.Exists(second.ReportPath), Is.True);
                Assert.That(File.Exists(foreign), Is.True);
                Assert.That(File.Exists(winmon), Is.True);
                Assert.That(MacCaptureReports.LoadRecords(directory).Single().Id, Is.EqualTo(second.Id));
                Assert.That(MacCaptureReports.LoadRecords(directory).Single().ReportPath, Is.EqualTo(second.ReportPath));
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void ReportUtcLabelUsesUtcEvenWhenSampleHasLocalOffset()
    {
        var sample = Sample(1) with { Timestamp = new DateTimeOffset(2026, 10, 25, 22, 0, 0, TimeSpan.FromHours(-5)) };
        Assert.That(MacCaptureReports.Text(sample, [], "test"), Does.Contain("UTC: 2026-10-26T03:00:00.0000000+00:00"));
    }

    [Test]
    public async Task FailedReportWriteIsReportedWithoutFaultingTheDrain()
    {
        var file = Path.GetTempFileName();
        try
        {
            string? reported = null;
            var sample = Sample(1);
            var record = MacCaptureReports.Create(new(new(CaptureMetric.Cpu, true, 1, 1), sample, sample, 50, [50]), file);
            var writer = new CaptureReportWriter((_, error) => reported = error);
            Assert.That(writer.Enqueue(record, "test"), Is.True);
            await writer.Complete();
            Assert.That(reported, Is.Not.Empty);
        }
        finally { File.Delete(file); }
    }
}
