using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MacMon;

public sealed partial class LaunchdView
{
    private bool _ascending = true;
    private bool _changingSort;
    public bool SortAscending { get => _ascending; set { _ascending = value; UpdateHeaders(); Rebuild(); } }
    private void OnJobHeaderClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var sort)) return;
        _ascending = SortIndex == sort ? !_ascending : sort < 3;
        _changingSort = true;
        try { SortIndex = sort; }
        finally { _changingSort = false; }
        UpdateHeaders(); Rebuild();
    }
    private void UpdateHeaders()
    {
        foreach (var (button, title) in new[] { (JobNameHeader, "Job / process"), (JobStateHeader, "Status"),
            (JobCpuHeader, "CPU"), (JobMemoryHeader, "Memory"), (JobIoHeader, "I/O") })
        {
            var selected = button.Tag?.ToString() == SortIndex.ToString();
            button.Classes.Set("sorted", selected);
            button.Content = UiText.Get(title) + (selected ? _ascending ? " ▴" : " ▾" : "");
            AutomationProperties.SetName(button, UiText.Get("Sort jobs") + ": " + button.Content);
            ToolTip.SetTip(button, UiText.Get("Click to sort; click again to reverse. Unavailable values remain last."));
        }
    }
    private void OnJobContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (sender is not Control { DataContext: LaunchdRow row } target) return;
        JobList.SelectedItem = row;
        MenuItem Item(string label, string icon, Button button) => TableInteractions.Item(label, icon, () =>
        {
            var current = (JobList.ItemsSource as IEnumerable<LaunchdRow> ?? []).FirstOrDefault(candidate =>
                candidate.Job.Id == row.Job.Id && candidate.Job.ObservedIdentity == row.Job.ObservedIdentity);
            if (current is not null)
            { JobList.SelectedItem = current; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            return Task.CompletedTask;
        }, button.IsEnabled);
        var menu = new ContextMenu { ItemsSource = new[] { Item("Properties", "Info", JobDetails),
            Item("Copy details", "Copy", CopyJob), Item("Workers", "Process", ShowWorkers), TableInteractions.Item("Refresh", "Live", Refresh, !_historical) } };
        target.ContextMenu = menu; menu.Open(target); args.Handled = true;
    }
}
