using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
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
    private string _operationStatus = "";
    private readonly ProcessPins _pins = new(MacFrameSource.IdentityPolicy);
    private readonly HistoryInspection _inspection = new();
    private int RangeSeconds => HistoryRange.SelectedIndex switch { 1 => 600, 2 => 1800, 3 => 3600, _ => 120 };
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
            var selected = _selection.Resolve(LiveHistory(), Grouped, _inspection.IsHistorical, Sort);
            if (selected is null) return;
            _pins.Toggle(selected.Usage, Grouped);
            UpdateRetention();
            UpdateProcesses();
        };
        Chart.SampleSelected += sample => { _inspection.Select(sample); Update(); };
        CaptureMode.Click += (_, _) => { if (_session.Latest is { } sample) _inspection.Select(sample); Update(); };
        LiveMode.Click += (_, _) => { _inspection.Live(); Update(); };
        HistoryRange.SelectionChanged += (_, _) =>
        {
            _session.History.Resize(RangeSeconds);
            Chart.RangeSeconds = RangeSeconds;
            Update();
        };
        ExportResources.Click += async (_, _) => await Export(CsvExportKind.ResourceHistory);
        ExportProcesses.Click += async (_, _) => await Export(CsvExportKind.ProcessSamples);
        Opened += (_, _) => _sampling = _session.Run(_shutdown.Token);
        Closed += OnClosed;
    }

    private void Update()
    {
        var sample = _session.Latest;
        if (sample is null)
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
        MemoryDetails.Text = sample.Resources.TotalMemoryBytes is null ? "Memory readings unavailable" : $"{GiB(sample.Resources.UsedMemoryBytes)} used / {GiB(sample.Resources.TotalMemoryBytes)} installed\n" +
            $"Pressure: {_source.Memory.Pressure} · compressed {GiB(_source.Memory.CompressedBytes)}";
        DiskValue.Text = GiB(sample.Resources.DiskFreeBytes);
        DiskTotal.Text = $"of {GiB(sample.Resources.DiskTotalBytes)}";
        var display = TrailHistory();
        var captured = display.LastOrDefault();
        Chart.SelectedTimestamp = _inspection.IsHistorical ? captured?.MonotonicTimestamp : null;
        Chart.Update(_session.History.Samples);
        CaptureStatus.Text = !_inspection.IsHistorical ? $"Live · recording · {_session.History.Samples.Count} retained samples" : captured is null
            ? "Capture expired from retained history · return to Live or select another moment"
            : $"Captured {captured.Timestamp:yyyy-MM-dd HH:mm:ss zzz} · CPU {captured.TotalCpuPercent?.ToString("F1") ?? "—"}% · memory {GiB(captured.Resources.UsedMemoryBytes)} · retained process observations";
        UpdateProcesses();
        StatusLabel.Text = _session.Error is null
            ? $"Updated {sample.Timestamp:HH:mm:ss} · macOS {Environment.OSVersion.Version}"
            : "Some readings are unavailable · retrying…";
        if (_operationStatus.Length > 0) StatusLabel.Text += $" · {_operationStatus}";
    }

    private ProcessSort Sort => ProcessSortBox.SelectedIndex switch
    {
        1 => ProcessSort.Memory,
        2 => ProcessSort.Threads,
        _ => ProcessSort.Cpu
    };

    private void UpdateProcesses()
    {
        if (_session is null) return;
        var display = TrailHistory();
        if (display.LastOrDefault() is not { } sample)
        {
            _refreshingRows = true;
            ProcessList.ItemsSource = Array.Empty<ProcessRow>();
            _refreshingRows = false;
            EmptyProcessLabel.IsVisible = true;
            EmptyProcessLabel.Text = "Selected capture is no longer retained";
            PinSelection.IsEnabled = false;
            ProcessCountLabel.Text = "Capture expired";
            SelectionLabel.Text = "Return to Live or select another moment.";
            return;
        }
        ResidentHeader.Text = Grouped ? "Resident sum*" : "Resident";
        var rows = ProcessPresentation.Rows(TrailHistory(), sample, Grouped, Sort,
            ProcessSearchBox.Text ?? "", _pins, PinnedOnly.IsChecked == true, _inspection.IsHistorical);
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
        var selected = _selection.Resolve(LiveHistory(), Grouped, _inspection.IsHistorical, Sort);
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
        return _inspection.DisplayHistory(_session.History.Samples, _session.LatestFull);
    }

    private IReadOnlyList<MonitorSample> LiveHistory()
    {
        return TrailHistory().TakeLast(ProcessRanking.DefaultWindowSeconds + 1).ToArray();
    }

    private async Task Export(CsvExportKind kind)
    {
        var samples = CsvExport.SelectSamples(_session.History.Samples, RangeSeconds).ToArray();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = kind == CsvExportKind.ResourceHistory ? "Export resource history" : "Export retained process samples",
            SuggestedFileName = $"MacMon-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) { _operationStatus = "Choose a local file for export."; return; }
        try
        {
            await Task.Run(() => CsvExport.Write(path, kind, samples, [], DateTimeOffset.Now, 0, 0));
            _operationStatus = $"Exported {samples.Length} retained samples to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _operationStatus = $"Export failed: {ex.Message}"; }
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
