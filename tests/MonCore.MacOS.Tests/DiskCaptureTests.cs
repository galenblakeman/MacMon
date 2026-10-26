using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using MacMon;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class DiskCaptureTests
{
    private static MonitorSample Sample(int second, double? activity, bool secondDisk, string name = "disk1") =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, [], 0, MonotonicTimestamp: (second + 1L) * Stopwatch.Frequency)
        { Resources = new() { DiskName = secondDisk ? "disk0" : name, DiskActivePercent = secondDisk ? 0 : activity,
            SecondaryDiskName = secondDisk ? name : "", SecondaryDiskActivePercent = secondDisk ? activity : null } };
    [TestCase(false)]
    [TestCase(true)]
    public void DiskRulesBreakOnMissingCountersAndGapsThenRearmWithCooldown(bool second)
    {
        var metric = second ? CaptureMetric.SecondaryDisk : CaptureMetric.PrimaryDisk;
        var rule = new CaptureRuleSetting(metric, true, 50, 2);
        var rules = new CaptureRules([rule], TimeSpan.FromSeconds(10), 8);
        Assert.That(rules.Observe(Sample(1, 80, second)), Is.Empty);
        Assert.That(rules.Observe(Sample(2, null, second)), Is.Empty);
        Assert.That(rules.Observe(Sample(3, 90, second)), Is.Empty);
        Assert.That(rules.Observe(Sample(4, 90, second) with { IsGap = true }), Is.Empty);
        Assert.That(rules.Observe(Sample(5, 80, second, "disk2")), Is.Empty);
        var fired = rules.Observe(Sample(6, 70, second, "disk2")).Single();
        Assert.That(fired.PeakValue, Is.EqualTo(80));
        Assert.That(fired.Peak.Resources, Is.EqualTo(Sample(5, 80, second, "disk2").Resources));
        Assert.That(MacCaptureReports.Create(fired, Path.GetTempPath()).Label, Does.Contain("estimated I/O activity"));
        Assert.That(MacCaptureReports.AutomaticText(fired), Does.Contain("Not Windows active time"));
        rules.Observe(Sample(7, 0, second)); rules.Observe(Sample(8, 0, second));
        Assert.That(rules.Observe(Sample(9, 80, second)), Is.Empty);
        Assert.That(rules.Observe(Sample(10, 80, second)), Is.Empty);
        Assert.That(rules.Observe(Sample(16, 80, second)), Has.Count.EqualTo(1));
        var absent = new CaptureRules([rule], TimeSpan.FromSeconds(1), 8);
        if (second)
        { Assert.That(absent.Observe(Sample(1, 90, true, "")), Is.Empty); Assert.That(absent.Observe(Sample(2, 90, true, "")), Is.Empty); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void SavedDiskReportsReloadWithTheirMetricAndPeak(bool second)
    {
        var path = Path.Combine(Path.GetTempPath(), "macmon-disk-capture-" + Guid.NewGuid());
        var metric = second ? CaptureMetric.SecondaryDisk : CaptureMetric.PrimaryDisk;
        var sample = Sample(1, 90, second);
        var fired = new CaptureFired(new(metric, true, 80, 1), sample, sample, 90, [90]);
        var record = MacCaptureReports.Create(fired, path);
        try
        {
            MacCaptureReports.Write(record, MacCaptureReports.AutomaticText(fired));
            var loaded = MacCaptureReports.LoadRecords(path).Single();
            Assert.That(loaded.Metric, Is.EqualTo(metric)); Assert.That(loaded.Peak, Is.EqualTo(record.Peak));
            Assert.That(File.ReadAllText(loaded.ReportPath), Does.Contain("Process I/O includes all devices"));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [AvaloniaTest]
    public void DiskSettingsRoundTripAndOldPreferencesDefaultBothRulesOff()
    {
        var old = new MacCaptureOptions(true, [new(CaptureMetric.Cpu, true, 90, 10)]).Normalized();
        Assert.That(old.Rules.Where(r => r.Metric is CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk).All(r => !r.Enabled), Is.True);
        var editor = new CaptureSettingsEditor(); editor.Load(old);
        editor.FindControl<CheckBox>("DiskEnabled")!.IsChecked = true;
        editor.FindControl<NumericUpDown>("DiskThreshold")!.Value = 72;
        editor.FindControl<CheckBox>("Disk2Enabled")!.IsChecked = true;
        editor.FindControl<NumericUpDown>("Disk2Seconds")!.Value = 12;
        var prefs = new MacPreferences { Captures = editor.Value };
        var restored = System.Text.Json.JsonSerializer.Deserialize<MacPreferences>(System.Text.Json.JsonSerializer.Serialize(prefs))!.Normalized(null);
        editor.Load(restored.Captures);
        Assert.That(editor.Value.Rules, Is.EqualTo(prefs.Captures.Rules));
        Assert.That(editor.Value.Rules.Single(r => r.Metric == CaptureMetric.PrimaryDisk).Threshold, Is.EqualTo(72));
        Assert.That(editor.Value.Rules.Single(r => r.Metric == CaptureMetric.SecondaryDisk).Seconds, Is.EqualTo(12));
    }
}
