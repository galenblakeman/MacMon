using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class LaunchdView : UserControl
{
    private readonly LaunchdMonitor _monitor;
    private readonly DispatcherTimer _heartbeat = new() { Interval = TimeSpan.FromSeconds(1) };
    private LaunchdInventory Inventory => _monitor.Inventory;
    private bool _active;
    private Task? _refresh;
    private bool _updating;
    private bool _stopped;
    private LaunchdJobId? _selected;
    private MonitorSample? _live;
    private long _liveAt;
    private readonly LaunchdHistory _jobHistory = new();
    private MonitorSample? _inspection;
    private MonitorSample[] _inspectionTrail = [];
    private bool _historical;
    private LaunchdSnapshot? DisplaySnapshot => _historical ? _jobHistory.At(_inspection?.MonotonicTimestamp ?? 0) : Inventory.LastSuccess;
    private MonitorSample? DisplaySample => _historical ? _inspection : _live;
    private bool DisplayFresh => _historical ? DisplaySnapshot is not null : Fresh && !Inventory.IsStale;
    public void SetInspection(MonitorSample? sample, bool historical)
    {
        if (_historical == historical && _inspection?.MonotonicTimestamp == sample?.MonotonicTimestamp && _inspection?.Timestamp == sample?.Timestamp) return;
        _historical = historical; _inspection = sample;
        _inspectionTrail = historical ? _history.Where(s => s.MonotonicTimestamp <= (sample?.MonotonicTimestamp ?? 0)).ToArray() : [];
        RefreshJobs.IsEnabled = !historical && !_monitor.IsRefreshing; Rebuild();
    }
    private readonly Queue<MonitorSample> _history = new();
    public event Action<string>? SummaryChanged;
    private string _scope = "Current bootstrap context";
    public string SummaryDetails => _scope + "\n" + JobStatus.Text;

    private static Func<CancellationToken, Task<LaunchdSnapshot>> NativeReader()
    {
        var reader = new MacLaunchdReader();
        return ct => Task.Run(() => reader.Read(ct), ct);
    }
    public LaunchdView() : this(NativeReader()) { }
    public LaunchdView(Func<CancellationToken, Task<LaunchdSnapshot>> read)
    {
        _monitor = new(read);
        InitializeComponent();
        _heartbeat.Tick += (_, _) =>
        {
            if (!VisibleForPolling) return;
            if (_monitor.IsDue(true)) _ = Refresh();
            else if (Inventory.IsStale) Rebuild();
        };
        _heartbeat.Start();
        ShowJobActions.IsCheckedChanged += (_, _) => ButtonIcon.SetKind(ShowJobActions, ShowJobActions.IsChecked == true ? "ChevronDown" : "ChevronUp");
        RefreshJobs.Click += async (_, _) => await Refresh();
        TableInteractions.AddSearchClear(JobSearch);
        JobSearch.TextChanged += (_, _) => Rebuild();
        JobFilter.SelectionChanged += (_, _) => Rebuild();
        JobSort.SelectionChanged += (_, _) => { if (!_changingSort) _ascending = JobSort.SelectedIndex < 3; UpdateHeaders(); Rebuild(); };
        JobLimit.ItemsSource = TablePresentation.Limits.Select(n => n == 0 ? "All" : n.ToString()).ToArray();
        JobLimit.SelectedIndex = 0;
        JobLimit.SelectionChanged += (_, _) => Rebuild();
        JobList.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _selected = (JobList.SelectedItem as LaunchdRow)?.Job.Id;
            JobDetails.IsEnabled = CopyJob.IsEnabled = JobList.SelectedItem is LaunchdRow;
            ShowWorkers.IsEnabled = JobList.SelectedItem is LaunchdRow { Process: not null };
            JobSelectionName.Text = (JobList.SelectedItem as LaunchdRow)?.Name ?? UiText.Get("Select a job");
            ToolTip.SetTip(JobSelectionName, (JobList.SelectedItem as LaunchdRow)?.Label);
            JobScope.Text = JobList.SelectedItem is LaunchdRow selected ? $"{selected.State} · {selected.ProcessDescription} · read only" : _historical ? UiText.Get("Captured launchd inventory") : "Current bootstrap context · read only";
        };
        ShowWorkers.Click += async (_, _) => await ShowWorkerTree();
        JobDetails.Click += async (_, _) =>
        {
            if (Details() is { } text && TopLevel.GetTopLevel(this) is Window window)
                await new ProcessDialog("launchd job details · read only", text).ShowDialog<bool>(window);
        };
        CopyJob.Click += async (_, _) =>
        {
            if (Details() is { } text && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                try { await clipboard.SetTextAsync(text); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                { JobStatus.Text = $"Copy failed: {ex.Message}"; }
            }
        };
    }

    public void ObserveLiveSample(MonitorSample sample)
    {
        _live = sample; _liveAt = Stopwatch.GetTimestamp();
        if (_history.TryPeek(out var first) && sample.MonotonicTimestamp <= first.MonotonicTimestamp) _history.Clear();
        _history.Enqueue(sample);
        while (_history.TryPeek(out var old) && (_history.Count > 31 ||
            (sample.MonotonicTimestamp - old.MonotonicTimestamp) / (double)Stopwatch.Frequency > 30)) _history.Dequeue();
        if (VisibleForPolling && Inventory.LastSuccess is not null) Rebuild();
    }
    private bool VisibleForPolling => _active && IsEffectivelyVisible &&
        TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: not WindowState.Minimized };
    public void SetActive(bool active)
    {
        _active = active;
        if (_monitor.IsDue(VisibleForPolling)) _ = Refresh();
        if (active) Rebuild();
    }
    public int SortIndex { get => JobSort.SelectedIndex; set => JobSort.SelectedIndex = Math.Clamp(value, 0, 5); }
    public int RowLimit { get => TablePresentation.Limits[Math.Clamp(JobLimit.SelectedIndex, 0, TablePresentation.Limits.Length - 1)]; set => JobLimit.SelectedIndex = Array.IndexOf(TablePresentation.Limits, TablePresentation.NormalizeLimit(value)); }
    public int FilterIndex { get => JobFilter.SelectedIndex; set => JobFilter.SelectedIndex = Math.Clamp(value, 0, 2); }
    public Task Refresh()
    {
        if (_stopped) return Task.CompletedTask;
        if (_refresh is { IsCompleted: false }) return _refresh;
        return _refresh = Read();
    }

    private async Task Read()
    {
        RefreshJobs.IsEnabled = false;
        if (!_historical) JobStatus.Text = "Reading loaded jobs in the current bootstrap context…";
        try
        {
            await _monitor.Refresh();
            if (_stopped) return;
            if (Fresh && !Inventory.IsStale && Inventory.LastSuccess is { } snapshot && _live is not null)
                _jobHistory.Record(snapshot, _live.MonotonicTimestamp + Stopwatch.GetTimestamp() - _liveAt);
            Rebuild();
        }
        finally { if (!_stopped) RefreshJobs.IsEnabled = !_historical; }
    }

    private bool Fresh => _liveAt > 0 && Stopwatch.GetElapsedTime(_liveAt).TotalSeconds <= 5;
    private void Rebuild()
    {
        if (JobList is null || _stopped) return;
        UpdateHeaders();
        var snapshot = DisplaySnapshot;
        var rows = LaunchdPresentation.Rows(snapshot, DisplaySample, DisplayFresh,
            JobSearch.Text, JobFilter.SelectedIndex, JobSort.SelectedIndex, RowLimit, _historical ? _inspectionTrail : _history.ToArray(), _ascending);
        var scroller = JobList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroller?.Offset;
        _updating = true;
        try
        {
            JobList.ItemsSource = rows;
            JobList.SelectedItem = rows.FirstOrDefault(row => row.Job.Id == _selected);
        }
        finally { _updating = false; }
        if (scroller is not null && offset is { } position) scroller.Offset = position;
        JobDetails.IsEnabled = CopyJob.IsEnabled = JobList.SelectedItem is LaunchdRow;
        ShowWorkers.IsEnabled = JobList.SelectedItem is LaunchdRow { Process: not null };
        JobSelectionName.Text = (JobList.SelectedItem as LaunchdRow)?.Name ?? UiText.Get("Select a job");
        ToolTip.SetTip(JobSelectionName, (JobList.SelectedItem as LaunchdRow)?.Label);
        JobEmpty.IsVisible = rows.Count == 0;
        JobEmpty.Text = snapshot is not null ? "No loaded jobs match this view." : Inventory.Error is null
            ? "Waiting for loaded jobs…" : "Job inventory unavailable · choose Refresh to retry.";
        _scope = (snapshot?.Context?.Display ?? (Inventory.Error is null ? "Current bootstrap context · waiting…" : "Current bootstrap context · unavailable")) +
            (Inventory.IsStale ? " · STALE" : "");
        JobStatus.Text = Inventory.Error is { } error
            ? $"Unavailable: {error}" + (snapshot is null ? "" : $" · STALE rows from {snapshot.ObservedAt:HH:mm:ss zzz}.")
            : snapshot is null ? "Waiting for discovery · refreshes automatically while visible."
            : $"{(Inventory.IsStale ? "STALE · " : "")}{rows.Count} shown / {snapshot.Jobs.Count} loaded · {snapshot.Jobs.Count(job => job.ReportedPid is not null)} running at {snapshot.ObservedAt:HH:mm:ss zzz}. Not running ≠ disabled. Refreshes every 5s while visible; captures use bounded session inventory.";
        SummaryChanged?.Invoke(snapshot is null ? Inventory.Error ?? "Waiting for discovery" :
            $"{(Inventory.IsStale ? "STALE · " : "")}{rows.Count} shown · {snapshot.Jobs.Count} jobs · {snapshot.Jobs.Count(job => job.ReportedPid is not null)} running");
        JobScope.Text = JobList.SelectedItem is LaunchdRow selected ? $"{selected.State} · {selected.ProcessDescription} · read only" : _historical ? UiText.Get("Captured launchd inventory") : "Current bootstrap context · read only";
        if (_historical)
        {
            _scope = UiText.Get("Captured launchd inventory") + (snapshot?.Context is { } context ? " · " + context.Display : "");
            JobStatus.Text = snapshot is null ? UiText.Get("No retained job inventory for this capture. Return to Live to observe jobs.")
                : $"{UiText.Get("Captured")} {_inspection?.Timestamp:yyyy-MM-dd HH:mm:ss zzz} · inventory {snapshot.ObservedAt:HH:mm:ss zzz} · {rows.Count} / {snapshot.Jobs.Count} jobs · read only";
            if (snapshot is null) JobEmpty.Text = JobStatus.Text;
            SummaryChanged?.Invoke(snapshot is null ? UiText.Get("Captured jobs unavailable") : $"{rows.Count} shown · {snapshot.Jobs.Count} captured jobs");
        }
        RefreshJobs.IsEnabled = !_historical && !_monitor.IsRefreshing;
        ToolTip.SetTip(JobScope, SummaryDetails);

    }

    private string? Details() => JobList.SelectedItem is LaunchdRow row && DisplaySnapshot is { } snapshot
        ? UiText.Get(_historical ? "Captured launchd inventory" : "Live") + "\n" +
          LaunchdPresentation.Details(row.Job, snapshot.ObservedAt, DisplaySample, DisplayFresh, !_historical && Inventory.IsStale) +
          "\n" + LaunchdWorkers.Build(row.Job, snapshot, DisplaySample, DisplayFresh).Text() : null;

    private async Task ShowWorkerTree()
    {
        if (JobList.SelectedItem is not LaunchdRow row || DisplaySnapshot is not { } snapshot ||
            TopLevel.GetTopLevel(this) is not Window window) return;
        var tree = LaunchdWorkers.Build(row.Job, snapshot, DisplaySample, DisplayFresh);
        if (tree.Root is null) return;
        await new JobWorkersWindow(row.Label, DisplaySample?.Timestamp ?? snapshot.ObservedAt, tree, _historical).ShowDialog(window);
    }

    public async Task Stop()
    {
        _stopped = true;
        _heartbeat.Stop();
        await _monitor.Stop();
        if (_refresh is not null) await _refresh;
    }
}
