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

    public LaunchdView()
    {
        var reader = new MacLaunchdReader();
        _monitor = new(ct => Task.Run(() => reader.Read(ct), ct));
        InitializeComponent();
        _heartbeat.Tick += (_, _) =>
        {
            if (!VisibleForPolling) return;
            if (_monitor.IsDue(true)) _ = Refresh();
            else if (Inventory.IsStale) Rebuild();
        };
        _heartbeat.Start();
        RefreshJobs.Click += async (_, _) => await Refresh();
        JobSearch.TextChanged += (_, _) => Rebuild();
        JobFilter.SelectionChanged += (_, _) => Rebuild();
        JobSort.SelectionChanged += (_, _) => Rebuild();
        JobList.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _selected = (JobList.SelectedItem as LaunchdRow)?.Job.Id;
            JobDetails.IsEnabled = CopyJob.IsEnabled = JobList.SelectedItem is LaunchdRow;
        };
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

    public void ObserveLiveSample(MonitorSample sample) { _live = sample; _liveAt = Stopwatch.GetTimestamp(); }
    private bool VisibleForPolling => _active && IsEffectivelyVisible &&
        TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: not WindowState.Minimized };
    public void SetActive(bool active)
    {
        _active = active;
        if (_monitor.IsDue(VisibleForPolling)) _ = Refresh();
        if (active) Rebuild();
    }
    public int SortIndex { get => JobSort.SelectedIndex; set => JobSort.SelectedIndex = Math.Clamp(value, 0, 2); }
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
        JobStatus.Text = "Reading loaded jobs in the current bootstrap context…";
        try
        {
            await _monitor.Refresh();
            if (_stopped) return;
            Rebuild();
        }
        finally { if (!_stopped) RefreshJobs.IsEnabled = true; }
    }

    private bool Fresh => _liveAt > 0 && Stopwatch.GetElapsedTime(_liveAt).TotalSeconds <= 5;
    private void Rebuild()
    {
        if (JobList is null || _stopped) return;
        var snapshot = Inventory.LastSuccess;
        var rows = LaunchdPresentation.Rows(snapshot, _live, Fresh && !Inventory.IsStale,
            JobSearch.Text, JobFilter.SelectedIndex, JobSort.SelectedIndex);
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
        JobEmpty.IsVisible = rows.Count == 0;
        JobEmpty.Text = snapshot is not null ? "No loaded jobs match this view." : Inventory.Error is null
            ? "Waiting for loaded jobs…" : "Job inventory unavailable · choose Refresh to retry.";
        JobScope.Text = (snapshot?.Context?.Display ?? (Inventory.Error is null ? "Current bootstrap context · waiting…" : "Current bootstrap context · unavailable")) +
            (Inventory.IsStale ? " · STALE" : "");
        JobStatus.Text = Inventory.Error is { } error
            ? $"Unavailable: {error}" + (snapshot is null ? "" : $" · STALE rows from {snapshot.ObservedAt:HH:mm:ss zzz}.")
            : snapshot is null ? "Waiting for discovery · refreshes automatically while visible."
            : $"{(Inventory.IsStale ? "STALE · " : "")}{rows.Count} shown / {snapshot.Jobs.Count} loaded · {snapshot.Jobs.Count(job => job.ReportedPid is not null)} running at {snapshot.ObservedAt:HH:mm:ss zzz}. Not running ≠ disabled. Refreshes every 5s while visible; separate from Capture.";
    }

    private string? Details() => JobList.SelectedItem is LaunchdRow row && Inventory.LastSuccess is { } snapshot
        ? LaunchdPresentation.Details(row.Job, snapshot.ObservedAt, _live, Fresh, Inventory.IsStale) : null;

    public async Task Stop()
    {
        _stopped = true;
        _heartbeat.Stop();
        await _monitor.Stop();
        if (_refresh is not null) await _refresh;
    }
}
