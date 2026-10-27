using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class NavigationInteractionTests
{
    private static AppUsage App(ProcessUsage p, MonitorSample sample, int running) => new(
        MacFrameSource.IdentityPolicy.ExecutableKey(p), p.Name, p.ImagePath, p.AppName, p.Publisher,
        10, 20, null, 1, running, false, SampleKey.Of(sample));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Update(MainWindow main) => typeof(MainWindow).GetMethod("UpdateProcesses", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
    private static MonitorSession Record(MainWindow main, MonitorSample sample)
    {
        var session = Field<MonitorSession>(main, "_session"); session.Record(sample, sample);
        typeof(MonitorSession).GetField("<HasLiveSample>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, true);
        Update(main); return session;
    }
    private static Window Host(MainWindow main)
    {
        Field<WindowViewState>(main, "_viewState").SetMode(false);
        main.FindControl<Grid>("MainLayout")!.IsVisible = true;
        main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
        main.FindControl<TabControl>("DataTabs")!.SelectedIndex = 0;
        var shell = main.Content; main.Content = null;
        var host = new Window { Width = 1000, Height = 800, Content = shell }; host.Show(); Dispatcher.UIThread.RunJobs(); return host;
    }
    [Test]
    public void UsageResolvesLiveGroupsAndExactLastActiveSamplesWithoutPidFallback()
    {
        var past = TablePresentationTests.Sample(); var p = past.Processes[1];
        var live = past with { Timestamp = past.Timestamp.AddSeconds(1), MonotonicTimestamp = past.MonotonicTimestamp + 1,
            Processes = [p with { CreationTicks = p.CreationTicks + 1 }] };
        Assert.That(UsageNavigation.Resolve(App(p, past, 1), [past, live], live)?.Sample, Is.SameAs(live));
        var exited = App(p, past, 0);
        Assert.That(UsageNavigation.Resolve(exited, [past, live], live), Is.EqualTo(new UsageProcessTarget(past, p, true)));
        Assert.That(UsageNavigation.Resolve(exited, [live], live), Is.Null);
        Assert.That(UsageNavigation.Resolve(exited with { LastActive = new(past.MonotonicTimestamp, live.Timestamp) }, [past, live], live), Is.Null);
        Assert.That(UsageNavigation.Resolve(exited with { ImagePath = p.ImagePath.ToLowerInvariant() }, [past], live), Is.Null);
        Assert.That(UsageNavigation.Resolve(exited with { Key = "another executable" }, [past], live), Is.Null);
        Assert.That(UsageNavigation.Resolve(exited with { LastActive = null }, [past], live), Is.Null);
    }
    [AvaloniaTest]
    public void ProcessActionsRetainIdentityWhenRankingAndFiltersHideTheRow()
    {
        var main = new MainWindow(); var window = Host(main);
        try
        {
            var sample = TablePresentationTests.Sample(); Record(main, sample);
            main.FindControl<RadioButton>("IndividualProcesses")!.IsChecked = true;
            main.FindControl<TextBox>("ProcessSearchBox")!.Text = "";
            main.FindControl<CheckBox>("ProcessAppsOnly")!.IsChecked = false;
            main.FindControl<CheckBox>("PinnedOnly")!.IsChecked = false;
            main.FindControl<ComboBox>("ProcessSortBox")!.SelectedIndex = 0;
            var cpuHeader = main.FindControl<Button>("ProcessCpuHeader")!;
            if (cpuHeader.Content!.ToString()!.EndsWith("▴"))
                cpuHeader.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var list = main.FindControl<ListBox>("ProcessList")!;
            list.SelectedItem = ((IEnumerable<ProcessRow>)list.ItemsSource!).First();
            var identity = ((ProcessRow)list.SelectedItem).Process.Identity;
            var name = main.FindControl<TextBlock>("ProcessSelectionName")!.Text;
            main.FindControl<ComboBox>("ProcessLimitBox")!.SelectedIndex = Array.IndexOf(TablePresentation.Limits, 5);
            main.FindControl<Button>("ProcessNameHeader")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.That(list.SelectedItem, Is.Null, "The selected high-CPU row is outside the first five names");
            Assert.That(main.FindControl<TextBlock>("ProcessSelectionName")!.Text, Is.EqualTo(name));
            main.FindControl<TextBox>("ProcessSearchBox")!.Text = "no matching executable";
            Dispatcher.UIThread.RunJobs();
            Assert.That(list.SelectedItem, Is.Null);
            Assert.That(main.FindControl<TextBlock>("ProcessSelectionName")!.Text, Is.EqualTo(name));
            Assert.That(main.FindControl<Button>("EndProcess")!.IsEnabled, Is.True);
            Assert.That(Field<ProcessSelection>(main, "_selection").Contains(sample.Processes.Single(p => p.Identity == identity), false), Is.True);
            var gone = sample with { Timestamp = sample.Timestamp.AddSeconds(1), MonotonicTimestamp = sample.MonotonicTimestamp + 1, Processes = [] };
            Record(main, gone);
            Assert.That(main.FindControl<Button>("EndProcess")!.IsEnabled, Is.False);
            Assert.That(main.FindControl<TextBlock>("ProcessSelectionName")!.Text, Is.EqualTo(name));
            main.FindControl<TextBox>("ProcessSearchBox")!.Text = "";
            Record(main, sample with { Timestamp = gone.Timestamp.AddSeconds(1), MonotonicTimestamp = gone.MonotonicTimestamp + 1,
                Processes = [sample.Processes.Single(p => p.Identity == identity) with { CreationTicks = 999999 }] });
            Assert.That(main.FindControl<Button>("EndProcess")!.IsEnabled, Is.False, "A reused PID must not become the selected lifetime");
        }
        finally { window.Close(); }
    }
    [AvaloniaTest]
    public void CaptureEntryOpensTheInteractiveCardAndEndReturnsToLive()
    {
        var main = new MainWindow(); var window = Host(main);
        try
        {
            UiText.Apply("en");
            var sample = TablePresentationTests.Sample(); var session = Record(main, sample);
            main.FindControl<Button>("CaptureMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var card = Field<CaptureCard>(main, "_captureCard");
            Assert.That(card.IsOpen, Is.True);
            window.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
            window.KeyRelease(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
            Assert.That(card.FindControl<Button>("CopyReading")!.IsFocused, Is.True);
            string? copied = null; card.CopyRequested += text => copied = text;
            session.Record(sample with { Timestamp = sample.Timestamp.AddSeconds(1), MonotonicTimestamp = sample.MonotonicTimestamp + 1, TotalCpuPercent = 99 });
            typeof(MainWindow).GetMethod("UpdateCaptureCard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            card.FindControl<Button>("CopyReading")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(copied, Does.Contain(sample.Timestamp.ToString("O")));
            Assert.That(card.IsOpen, Is.True);
            window.KeyPress(Avalonia.Input.Key.End, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
            window.KeyRelease(Avalonia.Input.Key.End, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
            Assert.That(card.IsOpen, Is.False);
            Assert.That(Field<HistoryInspection>(main, "_inspection").IsHistorical, Is.False);
        }
        finally { window.Close(); }
    }
    [AvaloniaTest]
    public void UsageDrilldownSelectsGroupAndKeepsExitedTargetsHistorical()
    {
        var main = new MainWindow(); var window = Host(main);
        try
        {
            var sample = TablePresentationTests.Sample(); var session = Record(main, sample); var p = sample.Processes[1];
            foreach (var running in new[] { 1, 0 })
            {
                session.Usage.Reset(sample.Timestamp);
                var row = new UsageRow(App(p, sample, running), 100);
                var list = main.FindControl<ListBox>("UsageList")!; list.ItemsSource = new[] { row }; list.SelectedItem = row;
                main.FindControl<Button>("UsageShowProcesses")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(main.FindControl<RadioButton>("GroupProcesses")!.IsChecked, Is.True);
                Assert.That(Field<ProcessSelection>(main, "_selection").Contains(p, true), Is.True);
                Assert.That(Field<HistoryInspection>(main, "_inspection").IsHistorical, Is.EqualTo(running == 0));
                Assert.That(main.FindControl<Button>("EndProcess")!.IsEnabled, Is.EqualTo(running > 0));
            }
            session.History.Resize(1);
            session.Record(sample with { Timestamp = sample.Timestamp.AddHours(1), MonotonicTimestamp = sample.MonotonicTimestamp + System.Diagnostics.Stopwatch.Frequency * 3600L });
            var expired = new UsageRow(App(p, sample, 0), 100);
            var usage = main.FindControl<ListBox>("UsageList")!; usage.ItemsSource = new[] { expired }; usage.SelectedItem = expired;
            main.FindControl<Button>("UsageShowProcesses")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(Field<string>(main, "_operationStatus"), Does.Contain("no longer retained"));
            Assert.That(Field<HistoryInspection>(main, "_inspection").IsHistorical, Is.True);
        }
        finally { window.Close(); }
    }
}
