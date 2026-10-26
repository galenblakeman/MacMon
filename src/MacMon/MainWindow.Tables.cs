using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class MainWindow
{
    private bool _restoringTables;
    private bool _changingTableSort;
    private bool _processAscending;
    private bool _usageAscending;
    private sealed record RowLimit(int Count)
    { public override string ToString() => Count == 0 ? UiText.Get("All") : Count.ToString(); }
    private static int SelectedLimit(ComboBox box) => (box.SelectedItem as RowLimit)?.Count ?? 0;
    private void InitializeTables()
    {
        ProcessLimitBox.ItemsSource = TablePresentation.Limits.Select(n => new RowLimit(n)).ToArray();
        UsageLimitBox.ItemsSource = TablePresentation.Limits.Select(n => new RowLimit(n)).ToArray();
        ProcessSortBox.SelectionChanged += (_, _) =>
        {
            if (!_changingTableSort && !_restoringTables) _processAscending = ProcessSortBox.SelectedIndex >= 4;
            UpdateProcessHeaders();
            if (!_restoringTables) UpdateProcesses();
        };
        UsageSortBox.SelectionChanged += (_, _) =>
        {
            if (!_changingTableSort && !_restoringTables) _usageAscending = UsageSortBox.SelectedIndex == 3;
            UpdateUsageHeaders();
            if (!_restoringTables) UpdateUsage();
        };
        ProcessLimitBox.SelectionChanged += (_, _) => { if (!_restoringTables) UpdateProcesses(); };
        UsageLimitBox.SelectionChanged += (_, _) => { if (!_restoringTables) UpdateUsage(); };
        ProcessAppsOnly.IsCheckedChanged += (_, _) => { if (!_restoringTables) UpdateProcesses(); };
        UsageAppsOnly.IsCheckedChanged += (_, _) => { if (!_restoringTables) UpdateUsage(); };
        ShowProcessActions.IsCheckedChanged += (_, _) => { if (!_restoringTables) UpdateSelection(); };
        InitializeUsageActions();
        CopyUsage.Click += async (_, _) => await CopyUsageRows();
    }
    private void RestoreTables()
    {
        _restoringTables = true;
        try
        {
            _processAscending = _preferences.ProcessAscending; _usageAscending = _preferences.UsageAscending;
            ProcessLimitBox.SelectedIndex = Array.IndexOf(TablePresentation.Limits, _preferences.ProcessLimit);
            UsageLimitBox.SelectedIndex = Array.IndexOf(TablePresentation.Limits, _preferences.UsageLimit);
            ProcessAppsOnly.IsChecked = _preferences.ProcessAppsOnly; UsageAppsOnly.IsChecked = _preferences.UsageAppsOnly;
            ShowProcessActions.IsChecked = !_preferences.ProcessActionsHidden;
        }
        finally { _restoringTables = false; }
        UpdateProcessHeaders(); UpdateUsageHeaders();
    }
    private ProcessRow[] PresentedProcesses(MonitorSample sample) => ProcessPresentation.Rows(TrailHistory(), sample, Grouped, Sort,
        ProcessSearchBox.Text ?? "", _pins, PinnedOnly.IsChecked == true, _inspection.IsHistorical,
        SelectedLimit(ProcessLimitBox), ProcessAppsOnly.IsChecked == true, _processAscending,
        ProcessSortBox.SelectedIndex switch { 4 => ProcessTableOrder.Name, 5 => ProcessTableOrder.Pid, _ => ProcessTableOrder.Metric },
        _historyOrigins.CanPin(sample, Grouped, _inspection.IsHistorical));
    private void OnProcessHeaderClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var sort)) return;
        _processAscending = ProcessSortBox.SelectedIndex == sort ? !_processAscending : sort >= 4;
        _changingTableSort = true;
        try { ProcessSortBox.SelectedIndex = sort; }
        finally { _changingTableSort = false; }
        UpdateProcessHeaders(); UpdateProcesses();
    }
    private void OnUsageHeaderClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var sort)) return;
        _usageAscending = UsageSortBox.SelectedIndex == sort ? !_usageAscending : sort == 3;
        _changingTableSort = true;
        try { UsageSortBox.SelectedIndex = sort; }
        finally { _changingTableSort = false; }
        UpdateUsageHeaders(); UpdateUsage();
    }
    private static void Header(Button button, string label, int sort, bool ascending, string table)
    {
        label = UiText.Get(label);
        var selected = button.Tag?.ToString() == sort.ToString();
        button.Content = label + (selected ? ascending ? " ↑" : " ↓" : "");
        AutomationProperties.SetName(button, $"Sort {table} by {label}" + (selected ? ascending ? ", ascending" : ", descending" : ""));
        ToolTip.SetTip(button, "Click to sort; click again to reverse. Unavailable values remain last.");
    }
    private void UpdateProcessHeaders()
    {
        (Button Button, string Label)[] headers = [(ProcessNameHeader, "Process"), (ProcessPidHeader, Grouped ? "Members" : "PID"),
            (ProcessCpuHeader, "CPU"), (ResidentHeader, Grouped ? "Memory Σ" : "Memory"),
            (ProcessThreadsHeader, "Threads"), (ProcessIoHeader, "Disk MiB/s")];
        if (ProcessSortBox.Items[5] is ComboBoxItem pidChoice) pidChoice.Content = Grouped ? "Member count" : "PID";
        foreach (var (button, label) in headers) Header(button, label, ProcessSortBox.SelectedIndex, _processAscending, "processes");
    }
    private void UpdateUsageHeaders()
    {
        (Button Button, string Label)[] headers = [(UsageNameHeader, "App"), (UsageCpuHeader, "CPU time"),
            (UsageShareHeader, "Share"), (UsageIoHeader, "I/O MiB"), (UsageProcessesHeader, "Processes"), (UsageLastHeader, "Last active")];
        foreach (var (button, label) in headers) Header(button, label, UsageSortBox.SelectedIndex, _usageAscending, "usage");
    }
}
