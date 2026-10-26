using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class TableInteractionTests
{
    [AvaloniaTest]
    public void ClearSearchResetsTheQueryAndReturnsKeyboardFocus()
    {
        var search = new TextBox(); TableInteractions.AddSearchClear(search);
        var window = new Window { Content = search };
        try
        {
            window.Show(); var clear = (Button)search.InnerRightContent!;
            Assert.That(clear.IsVisible, Is.False);
            search.Text = "path:/Applications/Editor.app/Contents/MacOS/Editor"; Dispatcher.UIThread.RunJobs();
            Assert.That(clear.IsVisible, Is.True);
            Assert.That(AutomationProperties.GetName(clear), Is.EqualTo(UiText.Get("Clear search")));
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.That(search.Text, Is.Empty); Assert.That(clear.IsVisible, Is.False); Assert.That(search.IsFocused, Is.True);
        }
        finally { window.Close(); }
    }
    [AvaloniaTest]
    public void JobsContextMenuTargetsTheRequestedRowAndOffersOnlyReadOnlyActions()
    {
        var view = new LaunchdView();
        var sample = TablePresentationTests.Sample(); var process = sample.Processes[0];
        var ctx = new LaunchdContext(Guid.NewGuid(), 501, "Aqua");
        var snapshot = new LaunchdSnapshot(sample.Timestamp, ctx, [new(new(ctx, "example.job"), process.ProcessId, 0, process.Identity)]);
        var list = view.FindControl<ListBox>("JobList")!;
        var row = LaunchdPresentation.Rows(snapshot, sample, true, "", 0, 0).Single(); list.ItemsSource = new[] { row };
        var window = new Window { Width = 800, Height = 700, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var border = list.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("dataRow"));
            border.RaiseEvent(new ContextRequestedEventArgs()); Dispatcher.UIThread.RunJobs();
            Assert.That(list.SelectedItem, Is.EqualTo(row)); Assert.That(border.ContextMenu!.IsOpen, Is.True);
            Assert.That(border.ContextMenu.Items.OfType<MenuItem>().Select(i => i.Header),
                Is.EqualTo(new[] { UiText.Get("Properties"), UiText.Get("Copy details"), UiText.Get("Refresh") }));
            border.ContextMenu.Close();
        }
        finally { window.Close(); }
    }
    [Test]
    public void UsageNavigationQueryMatchesExactCaseSensitiveExecutableBeforeLimiting()
    {
        var sample = TablePresentationTests.Sample(); var path = sample.Processes[1].ImagePath;
        var rows = ProcessPresentation.Rows([sample], sample, false, ProcessSort.Cpu, "path:" + path,
            new ProcessPins(MacFrameSource.IdentityPolicy), false, limit: 5);
        Assert.That(rows.Single().Process.ImagePath, Is.EqualTo(path));
        Assert.That(TableInteractions.Matches(sample.Processes[1], "path:" + path.ToLowerInvariant()), Is.False);
        Assert.That(TableInteractions.Matches(sample.Processes[1] with { ImagePath = path + "Helper" }, "path:" + path), Is.False);
    }
    [AvaloniaTest]
    public void JobsHeadersToggleDirectionAndDropdownResetsTheDefaultDirection()
    {
        var view = new LaunchdView();
        var header = view.FindControl<Button>("JobCpuHeader")!;
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(view.SortIndex, Is.EqualTo(3)); Assert.That(view.SortAscending, Is.False);
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.That(view.SortAscending, Is.True);
        view.SortIndex = 0; Assert.That(view.SortAscending, Is.True);
        view.SortIndex = 4; Assert.That(view.SortAscending, Is.False);
    }
    [Test]
    public void JobsSortBothWaysAndKeepMissingReadingsLast()
    {
        var sample = TablePresentationTests.Sample(); var ctx = new LaunchdContext(Guid.NewGuid(), 501, "Aqua");
        var processes = sample.Processes.Take(2).ToArray();
        var jobs = processes.Select(p => new LaunchdJob(new(ctx, p.Name), p.ProcessId, 0, p.Identity))
            .Append(new(new(ctx, "missing"), null, 0)).ToArray();
        var snapshot = new LaunchdSnapshot(sample.Timestamp, ctx, jobs);
        foreach (var ascending in new[] { true, false })
        {
            var rows = LaunchdPresentation.Rows(snapshot, sample, true, "", 0, 3, ascending: ascending);
            Assert.That(rows.Last().Label, Is.EqualTo("missing"));
            Assert.That(rows.First().Process!.CpuPercent, Is.EqualTo(ascending ? processes.Min(p => p.CpuPercent) : processes.Max(p => p.CpuPercent)));
        }
    }
}
