using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class RankingMenuTests
{
    [AvaloniaTest]
    public void RankingChoicesUseRequestedOrderAndNameOnlyWithAllRows()
    {
        var main = new MainWindow();
        var limit = main.FindControl<ComboBox>("ProcessLimitBox")!;
        var process = main.FindControl<ComboBox>("ProcessSortBox")!;
        var jobs = main.FindControl<LaunchdView>("JobsView")!;
        string[] Labels(ComboBox box) => box.Items.OfType<ComboBoxItem>().Select(c => c.Content!.ToString()!).ToArray();
        limit.SelectedIndex = 0; jobs.RowLimit = 0;
        Assert.That(Labels(process), Is.EqualTo(new[] { "CPU", "Memory", "I/O", "Threads", "Name" }));
        Assert.That(Labels(jobs.FindControl<ComboBox>("JobSort")!), Is.EqualTo(new[] { "CPU", "Memory", "I/O", "Name" }));
        Assert.That(Labels(main.FindControl<ComboBox>("UsageSortBox")!), Is.EqualTo(new[] { "CPU time", "I/O", "Processes", "Last active" }));
        process.SelectedIndex = 4; jobs.SortIndex = 0;
        limit.SelectedIndex = 1; jobs.RowLimit = 5;
        Assert.That(Labels(process), Does.Not.Contain("Name"));
        Assert.That(Labels(jobs.FindControl<ComboBox>("JobSort")!), Does.Not.Contain("Name"));
        Assert.That(process.SelectedIndex, Is.Zero);
        Assert.That(jobs.SortIndex, Is.EqualTo(3));

    }
}
