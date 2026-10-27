using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class MainWindow
{
    private RankingMenu _processRanking = null!, _usageRanking = null!;
    private ActionBarState _actionBars = null!;
    private bool _restoringTables;
    private bool _changingTableSort;
    private bool _processAscending;
    private bool _usageAscending;
    private sealed record RowLimit(int Count)
    { public override string ToString() => Count == 0 ? UiText.Get("All") : Count.ToString(); }
    private static int SelectedLimit(ComboBox box) => (box.SelectedItem as RowLimit)?.Count ?? 0;
    private void InitializeTables()
    {
        _actionBars = new(ShowProcessActions, JobsView.FindControl<Avalonia.Controls.Primitives.ToggleButton>("ShowJobActions")!, ShowUsageActions);
        TableInteractions.AddSearchClear(ProcessSearchBox);
        TableInteractions.AddSearchClear(UsageSearch);
        DataTabs.Loaded += (_, _) =>
        {
            // Fluent sets a local clip on this wrapper. Let header backgrounds bleed
            // horizontally; each table's ScrollViewer still clips its own contents.
            if (DataTabs.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(p => p.Name == "PART_SelectedContentHost")?.GetVisualParent() is { } contentPanel)
                contentPanel.ClipToBounds = false;
        };
        ProcessLimitBox.ItemsSource = TablePresentation.Limits.Select(n => new RowLimit(n)).ToArray();
        UsageLimitBox.ItemsSource = TablePresentation.Limits.Select(n => new RowLimit(n)).ToArray();
        _processRanking = new(ProcessSortBox, [(0, "CPU"), (1, "Memory"), (3, "I/O"), (2, "Threads"), (4, "Name")], 0, 4, [(5, "PID")]);
        _usageRanking = new(UsageSortBox, [(0, "CPU time"), (1, "I/O"), (5, "Processes"), (2, "Last active")], 0, headerChoices: [(3, "Name"), (4, "Share")]);
        _processRanking.Changed += () =>
        {
            if (!_changingTableSort && !_restoringTables) _processAscending = _processRanking.Sort >= 4;
            UpdateProcessHeaders();
            if (!_restoringTables) UpdateProcesses();
        };
        _usageRanking.Changed += () =>
        {
            if (!_changingTableSort && !_restoringTables) _usageAscending = _usageRanking.Sort == 3;
            UpdateUsageHeaders();
            if (!_restoringTables) UpdateUsage();
        };
        ProcessLimitBox.SelectionChanged += (_, _) => { _processRanking.All = SelectedLimit(ProcessLimitBox) == 0; if (!_restoringTables) UpdateProcesses(); };
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
            _actionBars.Hidden = _preferences.ActionsHidden ?? _preferences.ProcessActionsHidden;
        }
        finally { _restoringTables = false; }
        UpdateProcessHeaders(); UpdateUsageHeaders();
    }
    private ProcessRow[] PresentedProcesses(MonitorSample sample) => ProcessPresentation.Rows(TrailHistory(), sample, Grouped, Sort,
        ProcessSearchBox.Text ?? "", _pins, PinnedOnly.IsChecked == true, _inspection.IsHistorical,
        SelectedLimit(ProcessLimitBox), ProcessAppsOnly.IsChecked == true, _processAscending,
        _processRanking.Sort switch { 4 => ProcessTableOrder.Name, 5 => ProcessTableOrder.Pid, _ => ProcessTableOrder.Metric },
        _historyOrigins.CanPin(sample, Grouped, _inspection.IsHistorical));
    private void OnProcessHeaderClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var sort)) return;
        _processAscending = _processRanking.Sort == sort ? !_processAscending : sort >= 4;
        _changingTableSort = true;
        try { _processRanking.Sort = sort; }
        finally { _changingTableSort = false; }
        UpdateProcessHeaders(); UpdateProcesses();
    }
    private void OnUsageHeaderClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var sort)) return;
        _usageAscending = _usageRanking.Sort == sort ? !_usageAscending : sort == 3;
        _changingTableSort = true;
        try { _usageRanking.Sort = sort; }
        finally { _changingTableSort = false; }
        UpdateUsageHeaders(); UpdateUsage();
    }
    private static void Header(Button button, string label, int sort, bool ascending, string table)
    {
        label = UiText.Get(label);
        var selected = button.Tag?.ToString() == sort.ToString();
        button.Classes.Set("sorted", selected);
        button.Content = label + (selected ? ascending ? " ▴" : " ▾" : "");
        AutomationProperties.SetName(button, $"Sort {table} by {label}" + (selected ? ascending ? ", ascending" : ", descending" : ""));
        ToolTip.SetTip(button, "Click to sort; click again to reverse. Unavailable values remain last.");
    }
    private void UpdateProcessHeaders()
    {
        (Button Button, string Label)[] headers = [(ProcessNameHeader, "Process"), (ProcessPidHeader, Grouped ? "Members" : "PID"),
            (ProcessCpuHeader, "CPU"), (ResidentHeader, Grouped ? "Memory Σ" : "Memory"),
            (ProcessThreadsHeader, "Threads"), (ProcessIoHeader, "I/O")];
        foreach (var (button, label) in headers) Header(button, label, _processRanking.Sort, _processAscending, "processes");
    }
    private void UpdateUsageHeaders()
    {
        (Button Button, string Label)[] headers = [(UsageNameHeader, "App"), (UsageCpuHeader, "CPU time"),
            (UsageShareHeader, "Share"), (UsageIoHeader, "I/O"), (UsageProcessesHeader, "Processes"), (UsageLastHeader, "Last active")];
        foreach (var (button, label) in headers) Header(button, label, _usageRanking.Sort, _usageAscending, "usage");
    }
}
