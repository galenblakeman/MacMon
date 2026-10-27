using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MacMon;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class DialogLayoutTests
{
    [AvaloniaTest]
    public void ExportDialogKeepsDataAndTimeChoicesIndependent()
    {
        var dialog = new ExportDialog();
        try
        {
            dialog.Show();
            dialog.FindControl<RadioButton>("ProcessChoice")!.IsChecked = true;
            dialog.FindControl<RadioButton>("RangeChoice")!.IsChecked = true;
            Assert.That(dialog.Selection, Is.EqualTo(new ExportSelection(CsvExportKind.ProcessSamples, true)));
            dialog.FindControl<RadioButton>("UsageChoice")!.IsChecked = true;
            Assert.That(dialog.FindControl<StackPanel>("TimeChoices")!.IsVisible, Is.False);
            Assert.That(dialog.Selection.Kind, Is.EqualTo(CsvExportKind.UsageTotals));
            dialog.FindControl<RadioButton>("ResourceChoice")!.IsChecked = true;
            Assert.That(dialog.FindControl<StackPanel>("TimeChoices")!.IsVisible, Is.True);
            Assert.That(dialog.Selection.ChartRange, Is.True);
            Dispatcher.UIThread.RunJobs();
            using var frame = dialog.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
        }
        finally { dialog.Close(); }
    }
    [AvaloniaTest]
    public void ExportStartsWithTheActiveViewsData()
    {
        foreach (var compact in new[] { false, true })
        for (var tab = 0; tab < 3; tab++)
        {
            var expected = compact || tab == 1 ? CsvExportKind.ResourceHistory : tab == 2 ? CsvExportKind.UsageTotals : CsvExportKind.ProcessSamples;
            var dialog = new ExportDialog(ExportDialog.InitialKind(compact, tab));
            Assert.That(dialog.Selection, Is.EqualTo(new ExportSelection(expected, false)));
            dialog.Close();
        }
    }
    [AvaloniaTest]
    public void InfoRefreshReplacesTheSnapshotAndRendersLongValues()
    {
        var reads = 0;
        var dialog = new HardwareInfoWindow(() => [new("System", [new("Computer", new string('M', 150) + ++reads)])]);
        try
        {
            dialog.Show();
            dialog.FindControl<Button>("RefreshInfo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(((HardwareSection)dialog.FindControl<ItemsControl>("InfoSections")!.Items[0]!).Entries[0].Value, Does.EndWith("2"));
            Dispatcher.UIThread.RunJobs();
            using var frame = dialog.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
        }
        finally { dialog.Close(); }
    }
}
