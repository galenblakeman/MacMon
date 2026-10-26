using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class UsageAttributionTests
{
    [Test]
    public void AttributionHasMachineShareAndSurvivesEmptyFilteredReports()
    {
        var attribution = new UsageAttribution(5 * TimeSpan.TicksPerSecond, 20 * TimeSpan.TicksPerSecond);
        Assert.That(attribution.Cpu, Is.EqualTo("0:00:05")); Assert.That(attribution.Share, Is.EqualTo("25.0%"));
        var report = UsagePresentation.Report([], DateTimeOffset.UnixEpoch, attribution);
        Assert.That(report, Does.Contain("Not attributed\t0:00:05\t25.0%\t—"));
        Assert.That(new UsageAttribution(0, 0).Share, Is.EqualTo("—"));
    }
    [AvaloniaTest]
    public void AttributionFooterIsSeparateFromSelectableAppRows()
    {
        var main = new MainWindow(); main.FindControl<TabControl>("DataTabs")!.SelectedIndex = 2;
        var list = main.FindControl<ListBox>("UsageList")!;
        Assert.That(list.ItemCount, Is.Zero);
        Assert.That(main.FindControl<TextBlock>("UnattributedCpu")!.Text, Is.EqualTo("0:00:00"));
        Assert.That(main.FindControl<TextBlock>("UnattributedShare")!.Text, Is.EqualTo("—"));
        Assert.That(main.FindControl<Button>("UsageShowProcesses")!.IsEnabled, Is.False);
    }
}
