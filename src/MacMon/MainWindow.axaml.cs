using Avalonia.Controls;
using Avalonia.Threading;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly MonitorSession _session;
    private readonly MacFrameSource _source = new();
    private Task? _sampling;
    private ProcessSelection _selection = new(MacFrameSource.IdentityPolicy);
    private volatile Func<ProcessUsage, bool>? _retain;
    private bool _refreshingRows;
    private readonly ProcessPins _pins = new(MacFrameSource.IdentityPolicy);
    private bool Grouped => GroupProcesses.IsChecked == true;

    public MainWindow()
    {
        InitializeComponent();
        MachineLabel.Text = $"{Environment.MachineName} · {Environment.ProcessorCount} logical cores";
        _session = new MonitorSession(() => _source,
            () => new SamplingOptions(Retain: _retain),
            action => Dispatcher.UIThread.Post(action), identityPolicy: MacFrameSource.IdentityPolicy);
        _session.Recorded += Update;
        ProcessSearchBox.TextChanged += (_, _) => UpdateProcesses();
        ProcessSortBox.SelectionChanged += (_, _) => UpdateProcesses();
        ProcessList.SelectionChanged += (_, _) =>
        {
            if (_refreshingRows) return;
            if (ProcessList.SelectedItem is ProcessRow row) _selection.Select(row.Process, Grouped);
            else _selection = new ProcessSelection(MacFrameSource.IdentityPolicy);
            UpdateRetention();
            UpdateSelection();
        };
        GroupProcesses.IsCheckedChanged += (_, _) =>
        {
            _selection = new ProcessSelection(MacFrameSource.IdentityPolicy);
            UpdateRetention();
            UpdateProcesses();
        };
        PinnedOnly.IsCheckedChanged += (_, _) => UpdateProcesses();
        PinSelection.Click += (_, _) =>
        {
            var selected = _selection.Resolve(LiveHistory(), Grouped, false, Sort);
            if (selected is null) return;
            _pins.Toggle(selected.Usage, Grouped);
            UpdateRetention();
            UpdateProcesses();
        };
        Opened += (_, _) => _sampling = _session.Run(_shutdown.Token);
        Closed += OnClosed;
    }

    private void Update()
    {
        var sample = _session.Latest;
        if (sample is null || _session.Error == SamplingError.ReadingsUnavailable)
        {
            CpuValue.Text = MemoryValue.Text = DiskValue.Text = "—";
            KernelValue.Text = "Waiting for a fresh CPU interval…";
            DiskTotal.Text = "Capacity unavailable";
            MemoryDetails.Text = "Memory readings unavailable";
            StatusLabel.Text = "Readings unavailable · retrying…";
            _refreshingRows = true;
            ProcessList.ItemsSource = Array.Empty<ProcessRow>();
            _refreshingRows = false;
            EmptyProcessLabel.IsVisible = true;
            EmptyProcessLabel.Text = "Process readings unavailable · retrying…";
            SelectionLabel.Text = "Selected process readings unavailable.";
            return;
        }

        CpuValue.Text = sample.TotalCpuPercent is { } cpu ? $"{cpu:F1}%" : "—";
        KernelValue.Text = sample.Resources.CpuKernelPercent is { } kernel
            ? $"{kernel:F1}% system CPU" : "Waiting for a fresh CPU interval…";
        MemoryValue.Text = sample.Resources.MemoryPercent is { } percent ? $"{percent:F1}%" : "—";
        MemoryDetails.Text = $"{GiB(sample.Resources.UsedMemoryBytes)} used / {GiB(sample.Resources.TotalMemoryBytes)} installed\n" +
            $"Pressure: {_source.Memory.Pressure} · compressed {GiB(_source.Memory.CompressedBytes)}";
        DiskValue.Text = GiB(sample.Resources.DiskFreeBytes);
        DiskTotal.Text = $"of {GiB(sample.Resources.DiskTotalBytes)}";
        Chart.Update(_session.History.Samples);
        UpdateProcesses();
        StatusLabel.Text = _session.Error is null
            ? $"Updated {sample.Timestamp:HH:mm:ss} · macOS {Environment.OSVersion.Version}"
            : "Some readings are unavailable · retrying…";
    }

    private ProcessSort Sort => ProcessSortBox.SelectedIndex switch
    {
        1 => ProcessSort.Memory,
        2 => ProcessSort.Threads,
        _ => ProcessSort.Cpu
    };

    private void UpdateProcesses()
    {
        if (_session?.LatestFull is not { } sample) return;
        ResidentHeader.Text = Grouped ? "Resident sum*" : "Resident";
        var rows = ProcessPresentation.Rows(TrailHistory(), sample, Grouped, Sort,
            ProcessSearchBox.Text ?? "", _pins, PinnedOnly.IsChecked == true);
        _refreshingRows = true;
        ProcessList.ItemsSource = rows;
        ProcessList.SelectedItem = rows.FirstOrDefault(row => _selection.Contains(row.Process, Grouped));
        _refreshingRows = false;
        EmptyProcessLabel.IsVisible = rows.Length == 0;
        EmptyProcessLabel.Text = _session.Error == SamplingError.ProcessesUnavailable
            ? "Process enumeration unavailable · retrying…"
            : sample.IsGap ? "Establishing process baselines…" : "No matching processes";
        ProcessCountLabel.Text = $"{rows.Length} shown · {sample.Resources.ProcessCount?.ToString() ?? "—"} enumerated · " +
            $"{sample.UnavailableProcesses} unavailable";
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = _selection.Resolve(LiveHistory(), Grouped, false, Sort);
        PinSelection.IsEnabled = selected is not null;
        PinSelection.Content = selected is not null && _pins.Contains(selected.Usage, Grouped) ? "Unpin selected" : "Pin selected";
        SelectionLabel.Text = selected is null ? "Select a process to inspect its lifetime and path."
            : selected.State != PinnedProcessState.Present
                ? $"{selected.Usage.Name} · PID {selected.Usage.ProcessId} · no longer observed or unavailable"
                : $"{selected.Usage.Name} · PID {selected.Usage.ProcessId} · started " +
                  $"{selected.Usage.StartedAtUtc?.ToLocalTime().ToString("g") ?? "unavailable"}\n" +
                  (selected.Usage.ImagePath.Length > 0 ? selected.Usage.ImagePath : "Executable path unavailable");
    }

    private void UpdateRetention()
    {
        var selection = _selection.RetentionSnapshot();
        var pins = _pins.RetentionSnapshot();
        _retain = selection is null ? pins : pins is null ? selection : process => selection(process) || pins(process);
    }

    private IReadOnlyList<MonitorSample> TrailHistory()
    {
        var history = _session.History.Samples;
        return _session.LatestFull is { } full ? [.. history.SkipLast(1), full] : history;
    }

    private IReadOnlyList<MonitorSample> LiveHistory()
    {
        // Retain compacted history, but use the full current frame so every live idle process remains visible.
        var compacted = _session.History.Samples;
        return _session.LatestFull is { } full
            ? [.. compacted.TakeLast(ProcessRanking.DefaultWindowSeconds + 1).SkipLast(1), full]
            : compacted;
    }

    private static string GiB(ulong? bytes) => bytes is { } value ? $"{value / 1073741824d:F1} GiB" : "—";

    private async void OnClosed(object? sender, EventArgs args)
    {
        _session.Close();
        _session.Recorded -= Update;
        await _shutdown.CancelAsync();
        if (_sampling is not null) await _sampling;
        _shutdown.Dispose();
    }
}
