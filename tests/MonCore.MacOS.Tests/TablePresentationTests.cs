using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class TablePresentationTests
{
    internal static MonitorSample Sample() => new(DateTimeOffset.UnixEpoch.AddSeconds(1), 30,
        Enumerable.Range(1, 20).Select(i => new ProcessUsage(i + 100, i * 100, $"Example application {i:D2}", i, (ulong)i * 1048576, i * 2048)
        { ImagePath = i % 2 == 0 ? $"/Applications/Example{i}.app/Contents/MacOS/Example" : $"/usr/bin/worker{i}",
            ThreadCount = i, LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds }).ToArray(), 0,
        MonotonicTimestamp: 2 * Stopwatch.Frequency)
    { IndependentProcessReadings = true, IntervalSeconds = 1, IntervalCpuTicks = 8 * TimeSpan.TicksPerSecond };

    [Test]
    public void FilteringPrecedesRowLimitAndMatchingPinsAreNeverTruncated()
    {
        var sample = Sample(); var pins = new ProcessPins(MacFrameSource.IdentityPolicy);
        pins.Toggle(sample.Processes[1], false);
        var rows = ProcessPresentation.Rows([sample], sample, false, ProcessSort.Cpu, "", pins, false,
            limit: 5, appsOnly: true);
        Assert.That(rows, Has.Length.EqualTo(6));
        Assert.That(rows[0].Process.Identity, Is.EqualTo(sample.Processes[1].Identity));
        Assert.That(rows[0].Pinned, Is.True);
        Assert.That(rows.Skip(1).Select(row => row.Process.ProcessId), Is.EqualTo(new[] { 120, 118, 116, 114, 112 }));
        Assert.That(rows.All(row => row.Trail.Values.Count == 1), Is.True);
        Assert.That(ProcessPresentation.Rows([sample], sample, false, ProcessSort.Cpu, "worker", pins, false,
            limit: 5, appsOnly: true), Is.Empty);
        Assert.That(sample.Processes, Has.Count.EqualTo(20), "Presentation must not trim retained observations");
    }

    [Test]
    public void BothDirectionsKeepUnavailableMetricsLastAndPreserveIdentities()
    {
        var low = Sample().Processes[0]; var high = Sample().Processes[^1];
        var unknown = low with { ProcessId = 999, CreationTicks = 999, CpuAvailable = false, WorkingSetBytes = null, ThreadCount = null };
        ProcessRow[] rows = [new(unknown), new(high), new(low)];
        foreach (var metric in new[] { ProcessSort.Cpu, ProcessSort.Memory, ProcessSort.Threads })
        {
            Assert.That(TablePresentation.Sort(rows, metric, ProcessTableOrder.Metric, false).Last().Process.Identity, Is.EqualTo(unknown.Identity));
            Assert.That(TablePresentation.Sort(rows, metric, ProcessTableOrder.Metric, true).Select(row => row.Process.Identity),
                Is.EqualTo(new[] { low.Identity, high.Identity, unknown.Identity }));
        }
        Assert.That(TablePresentation.Sort(rows, ProcessSort.Cpu, ProcessTableOrder.Pid, false).First().Process.ProcessId, Is.EqualTo(999));
        Assert.That(TablePresentation.Sort(rows, ProcessSort.Cpu, ProcessTableOrder.Name, true).Last().Process.Name, Is.EqualTo(high.Name));
    }

    [Test]
    public void GroupMemberHeaderOrdersTheDisplayedCountRatherThanRepresentativePid()
    {
        var sample = Sample();
        var small = sample.Processes[0] with { MemberCount = 2, ProcessId = 999 };
        var large = sample.Processes[1] with { MemberCount = 8, ProcessId = 1 };
        var rows = TablePresentation.Sort([new(small, Grouped: true), new(large, Grouped: true)],
            ProcessSort.Cpu, ProcessTableOrder.Pid, false).ToArray();
        Assert.That(rows[0].Pid, Is.EqualTo("8 procs"));
    }

    [Test]
    public void UsageFiltersAndLimitsDoNotLoseExitedAppsOrChangeLedgerTotals()
    {
        var ledger = new UsageLedger(DateTimeOffset.UnixEpoch, MacFrameSource.IdentityPolicy);
        var sample = Sample(); ledger.Add(sample);
        ledger.Add(sample with { Timestamp = sample.Timestamp.AddSeconds(1), MonotonicTimestamp = sample.MonotonicTimestamp + Stopwatch.Frequency, Processes = [] });
        var cpu = ledger.Apps.Sum(app => app.CpuTicks);
        var rows = UsagePresentation.Rows(ledger, "", UsageSort.Cpu, 5, true);
        Assert.That(rows, Has.Length.EqualTo(5));
        Assert.That(rows.All(row => row.App.Running == 0 && TablePresentation.ApplicationBundle(row.Path) is not null), Is.True);
        Assert.That(rows.Select(row => row.App.CpuTicks), Is.Ordered.Descending);
        Assert.That(UsagePresentation.Rows(ledger, "", UsageSort.Name, 5, true, true).Select(row => row.Name), Is.Ordered);
        Assert.That(UsagePresentation.Rows(ledger, "worker", UsageSort.DiskIo, 5, true), Is.Empty);
        Assert.That(ledger.Apps, Has.Count.EqualTo(20));
        Assert.That(ledger.Apps.Sum(app => app.CpuTicks), Is.EqualTo(cpu));
    }

    [Test]
    public void BundleMembershipIsConservativeAndKeepsNestedHelperIdentity()
    {
        Assert.That(TablePresentation.ApplicationBundle("/Applications/A.app/Contents/MacOS/A"), Is.EqualTo("/Applications/A.app"));
        Assert.That(TablePresentation.ApplicationBundle("/Applications/A.app/Contents/Frameworks/Helper.app/Contents/MacOS/Helper"),
            Is.EqualTo("/Applications/A.app/Contents/Frameworks/Helper.app"));
        foreach (var path in new[] { "", "A.app/Contents/MacOS/A", "/usr/bin/A", "/A.app/Contents/MacOS/", "/A.app/Contents/MacOS/sub/A", "/A.app/../Contents/MacOS/A", "/A.app/Contents/Resources/A" })
            Assert.That(TablePresentation.ApplicationBundle(path), Is.Null, path);
    }

    [Test]
    public void TablePreferencesRoundTripWithoutAffectingMonitoringOrPins()
    {
        var expected = new MacPreferences { ProcessSort = 5, UsageSort = 3, ProcessAscending = true, UsageAscending = true,
            ProcessLimit = 20, UsageLimit = 50, ProcessAppsOnly = true, UsageAppsOnly = true, ProcessActionsHidden = true,
            Grouped = true, KeepHistory = true, VisibleMetrics = 12 };
        var restored = System.Text.Json.JsonSerializer.Deserialize<MacPreferences>(System.Text.Json.JsonSerializer.Serialize(expected))!.Normalized(null);
        var settings = SettingsValues.From(restored).ApplyTo(restored, null);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(settings), Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(expected.Normalized(null))));
        Assert.That(new MacPreferences { ProcessLimit = -1, UsageLimit = 999 }.Normalized(null).ProcessLimit, Is.Zero);
        Assert.That(new MacPreferences { ProcessLimit = -1, UsageLimit = 999 }.Normalized(null).UsageLimit, Is.Zero);
    }

    [Test]
    public void UsageFormattingMatchesWinMonDurationAndSeenRunningConventions()
    {
        var ledger = new UsageLedger(DateTimeOffset.UnixEpoch, MacFrameSource.IdentityPolicy);
        var sample = Sample(); ledger.Add(sample);
        var row = UsagePresentation.Rows(ledger, "", UsageSort.Cpu, latest: sample.Timestamp).First();
        Assert.That(row.Cpu, Does.Match(@"^\d+:\d{2}:\d{2}$"));
        Assert.That(row.Processes, Is.EqualTo("1 (1)"));
        Assert.That(row.DiskIo, Is.EqualTo("40.00 KiB"));
        Assert.That(new ProcessRow(sample.Processes[0]).Io, Is.EqualTo("2.00 KiB/s"));
        Assert.That(row.LastActive, Is.EqualTo("now"));
        var report = UsagePresentation.Report([row], ledger.Since);
        Assert.That(report, Does.Contain("Seen (running)").And.Contain(row.Name).And.Contain("1 (1)"));
        Assert.That(report.Split('\n'), Has.Length.EqualTo(3), "Copy contains only the displayed rows");
    }

    [AvaloniaTest]
    public void HeaderActionsReverseDirectionAndKeepSortCombosSynchronized()
    {
        var main = new MainWindow();
        foreach (var header in new[] { "ProcessIoHeader", "UsageIoHeader" })
            Assert.That(main.FindControl<Button>(header)!.Content?.ToString(), Does.Not.Contain("MiB"));
        foreach (var prefix in new[] { "Process", "Usage" })
        {
            var name = main.FindControl<Button>(prefix + "NameHeader")!;
            var combo = main.FindControl<ComboBox>(prefix == "Process" ? "ProcessSortBox" : "UsageSortBox")!;
            var cpu = main.FindControl<Button>(prefix + "CpuHeader")!;
            combo.SelectedIndex = 0;
            name.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(combo.SelectedIndex, Is.EqualTo(prefix == "Process" ? 4 : 3));
            Assert.That(name.Content?.ToString(), Does.EndWith("↑"));
            name.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(name.Content?.ToString(), Does.EndWith("↓"));
            Assert.That(AutomationProperties.GetName(name), Does.Contain("descending"));
            cpu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(combo.SelectedIndex, Is.Zero);
            Assert.That(cpu.Content?.ToString(), Does.EndWith("↓"));
        }
    }

    [AvaloniaTest]
    public void NativeBundleIconUsesSmallCachedBitmapAndUnknownPathsUseFallback()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("Native AppKit check");
        const string path = "/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal";
        if (!File.Exists(path)) Assert.Ignore("Terminal bundle unavailable");
        var icon = ApplicationIcons.For(path);
        Assert.That(icon, Is.InstanceOf<Bitmap>());
        Assert.That(icon.Size.Width, Is.LessThanOrEqualTo(32));
        Assert.That(ApplicationIcons.For(path), Is.SameAs(icon));
        Assert.That(ApplicationIcons.For("/does-not-exist.app/Contents/MacOS/Missing"), Is.Not.Null);
    }

    [AvaloniaTest]
    public void JobsMatchTableLayoutAndKeepUnavailableResourcesExplicit()
    {
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var main = new MainWindow();
            ((App)Application.Current!).ApplyAppearance(theme, "Classic", "Bowl");
            main.FindControl<Grid>("MainLayout")!.IsVisible = true;
            main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
            main.FindControl<TabControl>("DataTabs")!.SelectedIndex = 1;
            var view = main.FindControl<LaunchdView>("JobsView")!;
            view.SetActive(false); // This is a presentation fixture, not live launchctl discovery.
            var sample = Sample();
            var context = new LaunchdContext(Guid.Empty, 501, "Aqua");
            var jobs = sample.Processes.Select(p => new LaunchdJob(new(context, "example." + p.ProcessId), p.ProcessId, 0, p.Identity)).ToArray();
            var snapshot = new LaunchdSnapshot(sample.Timestamp, context, jobs);
            var list = view.FindControl<ListBox>("JobList")!;
            list.ItemsSource = LaunchdPresentation.Rows(snapshot, sample, true, "", 0, 3, history: [sample]);
            view.FindControl<TextBlock>("JobEmpty")!.IsVisible = false;
            main.FindControl<TextBlock>("JobSummary")!.Text = "20 shown · 20 jobs · 20 running";
            var shell = main.Content; main.Content = null;
            var window = new Window { Width = 800, Height = 760, Content = shell };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.That(items.Length, Is.GreaterThan(2));
            Assert.That(items.All(item => item.Bounds.Height <= 44), Is.True);
            var cpuCell = items[0].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("cpuMetric"));
            var cpuHeader = view.FindControl<TextBlock>("JobCpuHeader")!;
            Assert.That(cpuHeader.TranslatePoint(new(cpuHeader.Bounds.Width, 0), window)!.Value.X,
                Is.EqualTo(cpuCell.TranslatePoint(new(cpuCell.Bounds.Width, 0), window)!.Value.X).Within(1), "Job CPU column alignment");
            Assert.That(view.FindControl<ComboBox>("JobLimit")!.SelectedItem?.ToString(), Is.EqualTo("All"));
            using var frame = window.CaptureRenderedFrame(); Assert.That(frame, Is.Not.Null);
            if (output is not null) frame!.Save(Path.Combine(output, $"table-jobs-800-{theme}.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
    }

    [AvaloniaTest]
    public void DenseTablesRenderWithIconsAndRowsAtMinimumAndDefaultSizes()
    {
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT");
        if (output is not null) Directory.CreateDirectory(output);
        foreach (var size in new[] { new Size(800, 760), new Size(1000, 900) })
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var tab in new[] { 0, 2 })
        {
            var main = new MainWindow();
            ((App)Application.Current!).ApplyAppearance(theme, "Classic", "Bowl");
            main.FindControl<Grid>("MainLayout")!.IsVisible = true;
            main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
            main.FindControl<TabControl>("DataTabs")!.SelectedIndex = tab;
            var sample = Sample();
            var list = main.FindControl<ListBox>(tab == 0 ? "ProcessList" : "UsageList")!;
            if (tab == 0)
            {
                list.ItemsSource = ProcessPresentation.Rows([sample], sample, false, ProcessSort.Cpu, "", new(MacFrameSource.IdentityPolicy), false);
                main.FindControl<TextBlock>("EmptyProcessLabel")!.IsVisible = false;
            }
            else
            {
                var ledger = new UsageLedger(DateTimeOffset.UnixEpoch, MacFrameSource.IdentityPolicy); ledger.Add(sample);
                list.ItemsSource = UsagePresentation.Rows(ledger, "", UsageSort.Cpu);
            }
            var shell = main.Content; main.Content = null;
            var window = new Window { Width = size.Width, Height = size.Height, Content = shell };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.That(items.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(items.All(item => item.Bounds.Height <= 44), Is.True);
            var cpuCell = items[0].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("cpuMetric"));
            var cpuHeader = main.FindControl<Button>(tab == 0 ? "ProcessCpuHeader" : "UsageCpuHeader")!;
            Assert.That(cpuHeader.TranslatePoint(new(cpuHeader.Bounds.Width, 0), window)!.Value.X,
                Is.EqualTo(cpuCell.TranslatePoint(new(cpuCell.Bounds.Width, 0), window)!.Value.X).Within(1), "Numeric header and row alignment");
            foreach (var item in items)
                Assert.That(item.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("dataRow") && b.BorderThickness.Bottom == 1), Is.True);
            if (tab == 0)
            {
                var pin = items[0].GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("rowPin"));
                Assert.That(pin.Bounds.Width, Is.LessThanOrEqualTo(28), "Pin action must stay inside its dedicated column");
                Assert.That(pin.TranslatePoint(default, window)!.Value.X,
                    Is.LessThan(main.FindControl<Button>("ProcessNameHeader")!.TranslatePoint(default, window)!.Value.X));
            }
            var limit = main.FindControl<ComboBox>(tab == 0 ? "ProcessLimitBox" : "UsageLimitBox")!;
            Assert.That(limit.TranslatePoint(default, window)!.Value.X + limit.Bounds.Width, Is.LessThanOrEqualTo(size.Width));
            using var frame = window.CaptureRenderedFrame(); Assert.That(frame, Is.Not.Null);
            if (output is not null) frame!.Save(Path.Combine(output, $"table-{tab}-{size.Width}-{theme}.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
    }
}
