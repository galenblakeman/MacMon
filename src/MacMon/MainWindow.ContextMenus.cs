using Avalonia.Controls;
using Avalonia.Input;
using MonCore.MacOS;

namespace MacMon;

public sealed partial class MainWindow
{
    private bool SelectContextProcess(ProcessRow row)
    {
        if (row.Grouped != Grouped) return false;
        var current = (ProcessList.ItemsSource as IEnumerable<ProcessRow> ?? []).FirstOrDefault(candidate =>
            candidate.Grouped == row.Grouped && (Grouped
                ? MacFrameSource.IdentityPolicy.Comparer.Equals(MacFrameSource.IdentityPolicy.ExecutableKey(candidate.Process), MacFrameSource.IdentityPolicy.ExecutableKey(row.Process))
                : candidate.Process.Identity == row.Process.Identity));
        if (current is null) return false;
        ProcessList.SelectedItem = current; UpdateSelection(); return true;
    }
    private void OnProcessContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (sender is not Control { DataContext: ProcessRow row } target || !SelectContextProcess(row)) return;
        MenuItem Item(string label, string icon, Func<Task> action, bool enabled = true) => TableInteractions.Item(label, icon,
            async () => { if (SelectContextProcess(row)) await action(); }, enabled);
        var menu = new ContextMenu { ItemsSource = new Control[]
        {
            Item("Properties", "Info", ShowProcessDetails), Item("Copy details", "Copy", CopyProcess),
            Item(row.Pinned ? "Unpin selected" : "Pin selected", "Pin", () =>
            {
                if (row.CanPin && _historyOrigins.CanPin(TrailHistory().LastOrDefault(), Grouped, _inspection.IsHistorical))
                { _pins.Toggle(row.Process, Grouped); UpdateRetention(); UpdateProcesses(); }
                return Task.CompletedTask;
            }, row.CanPin),
            Item("Reveal in Finder", "Folder", () => { RevealSelectedExecutable(); return Task.CompletedTask; }, File.Exists(row.Process.ImagePath)),
            new Separator(), Item(Grouped ? "End group…" : "End process…", "Cancel", EndSelectedProcesses, EndProcess.IsEnabled)
        } };
        target.ContextMenu = menu; menu.Open(target); args.Handled = true;
    }
    private void NavigateToProcesses(UsageRow row)
    {
        if (string.IsNullOrEmpty(row.Path)) return;
        ReturnToLive(); PinnedOnly.IsChecked = false; ProcessAppsOnly.IsChecked = false;
        ProcessSearchBox.Text = "path:" + row.Path; DataTabs.SelectedIndex = 0; UpdateProcesses(); ProcessSearchBox.Focus();
    }
    private void OnUsageContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (sender is not Control { DataContext: UsageRow row } target) return;
        UsageList.SelectedItem = row;
        MenuItem Item(string label, string icon, Button button) => TableInteractions.Item(label, icon, () =>
        {
            var current = (UsageList.ItemsSource as IEnumerable<UsageRow> ?? []).FirstOrDefault(candidate => candidate.App.Key == row.App.Key);
            if (current is not null)
            { UsageList.SelectedItem = current; button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
            return Task.CompletedTask;
        }, button.IsEnabled);
        var menu = new ContextMenu { ItemsSource = new[] { Item("Properties", "Info", UsageProperties),
            Item("Copy details", "Copy", CopyUsageApp), Item("Show processes", "Process", UsageShowProcesses),
            Item("Reveal in Finder", "Folder", RevealUsageApp) } };
        target.ContextMenu = menu; menu.Open(target); args.Handled = true;
    }
}
