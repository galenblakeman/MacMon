using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class LaunchdView : UserControl
{
    private readonly MacLaunchdReader _reader = new();
    private readonly LaunchdInventory _inventory = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _refresh;
    private bool _updating;
    private bool _stopped;
    private LaunchdJobId? _selected;
    private MonitorSample? _live;
    private long _liveAt;

    public LaunchdView()
    {
        InitializeComponent();
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
    public void Activate() { if (_inventory.LastSuccess is null && !_stopped) _ = Refresh(); }
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
            var snapshot = await Task.Run(() => _reader.Read(_stop.Token), _stop.Token);
            if (_stopped) return;
            _inventory.Apply(snapshot);
            Rebuild();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { if (!_stopped) RefreshJobs.IsEnabled = true; }
    }

    private bool Fresh => _liveAt > 0 && Stopwatch.GetElapsedTime(_liveAt).TotalSeconds <= 5;
    private void Rebuild()
    {
        if (JobList is null || _stopped) return;
        var snapshot = _inventory.LastSuccess;
        var rows = LaunchdPresentation.Rows(snapshot, _live, Fresh && _inventory.Error is null,
            JobSearch.Text, JobFilter.SelectedIndex, JobSort.SelectedIndex);
        _updating = true;
        try
        {
            JobList.ItemsSource = rows;
            JobList.SelectedItem = rows.FirstOrDefault(row => row.Job.Id == _selected);
        }
        finally { _updating = false; }
        JobDetails.IsEnabled = CopyJob.IsEnabled = JobList.SelectedItem is LaunchdRow;
        JobEmpty.IsVisible = rows.Count == 0;
        JobEmpty.Text = snapshot is null ? "Job inventory unavailable · choose Refresh to retry." : "No loaded jobs match this view.";
        JobScope.Text = snapshot?.Context?.Display ?? "Current bootstrap context · unavailable";
        JobStatus.Text = _inventory.Error is { } error
            ? $"Unavailable: {error}" + (snapshot is null ? "" : $" · STALE rows from {snapshot.ObservedAt:HH:mm:ss zzz}.")
            : snapshot is null ? "Choose Refresh to read loaded jobs."
            : $"{rows.Count} shown / {snapshot.Jobs.Count} loaded · {snapshot.Jobs.Count(job => job.ReportedPid is not null)} running at {snapshot.ObservedAt:HH:mm:ss zzz}. Not running ≠ disabled. Live inventory; separate from Capture.";
    }

    private string? Details() => JobList.SelectedItem is LaunchdRow row && _inventory.LastSuccess is { } snapshot
        ? LaunchdPresentation.Details(row.Job, snapshot.ObservedAt, _live, Fresh, _inventory.Error is not null) : null;

    public async Task Stop()
    {
        _stopped = true;
        await _stop.CancelAsync();
        if (_refresh is not null) await _refresh;
        _stop.Dispose();
    }
}
