using System.Diagnostics;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class GpuMonitoringTests
{
    [Test]
    public void MissingAndInvalidGpuUtilizationNeverBecomeZeroOrAFullGauge()
    {
        foreach (var value in new double?[] { null, -1, 101, double.NaN, double.PositiveInfinity })
            Assert.That(DarwinGpuReader.Validate(value), Is.Null);
        Assert.That(DarwinGpuReader.Validate(0), Is.Zero);
        Assert.That(DarwinGpuReader.Validate(100), Is.EqualTo(100));
    }
    [Test]
    public void GpuUsesPercentAndResetsPeaksWhenItsDeviceChangesOrDisappears()
    {
        MonitorSample Sample(int second, string id, double? value) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), 0, [], 0,
            false, (second + 1L) * Stopwatch.Frequency) { Resources = new() { GpuId = id, GpuName = "Test GPU", GpuPercent = value } };
        var history = new ResourceGaugeHistory();
        history.Observe(Sample(0, "one", 90)); history.Observe(Sample(1, "one", 10));
        Assert.That(history.Readings[4].Peak, Is.EqualTo(90));
        history.Observe(Sample(2, "two", 20));
        Assert.That(history.Readings[4].Peak, Is.EqualTo(20));
        Assert.That(history.Readings[4].Scale, Is.EqualTo("0–100%"));
        history.Observe(Sample(3, "two", null));
        Assert.That(history.Readings[4].Value, Is.Null);
        history.Observe(Sample(4, "two", 0));
        Assert.That(history.Readings[4].Peak, Is.Zero);
        Assert.That(ResourceCharts.Metrics(31), Is.EqualTo(new[] { 0, 1, 3, 2, 4 }));
        Assert.That(ResourceCharts.Maximum([Sample(1, "one", 20)], 120, 4), Is.EqualTo(100));
        var model = ResourceDetails.Create(ResourceMetric.Gpu, Sample(1, "one", 20), null, history.Readings[4], 4);
        Assert.That(model.Title, Is.EqualTo("GPU details"));
        Assert.That(model.Rows.Single(r => r.Label == "Shared memory in use").Value, Is.EqualTo("—"));
    }
}
