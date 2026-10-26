using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.Input;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly MonitorSession _session;
    private readonly MacFrameSource _source = new();
    private Task? _sampling;
    private Task? _startup;
    private readonly string _historyPath = Path.Combine(MacPaths.AppSupport, "history.mmh.gz");
    private readonly HistoryProvenance _historyOrigins;
    private bool _historyWritable = true;
    private readonly CaptureReportWriter _reportWriter;
    private readonly List<MacCaptureRecord> _captures = [];
    private MacCaptureOptions _captureOptions = MacCaptureOptions.Default;
    private CaptureRules _captureRules = new([], TimeSpan.FromSeconds(60), Environment.ProcessorCount);
    private bool _refreshingCaptures;
    private bool _closing;
    private bool _shutdownComplete;
    private readonly string? _bootId = MacBootIdentity.Read();
    private readonly string _preferencesPath = Path.Combine(MacPaths.AppSupport, "settings.json");
    private MacPreferences _preferences = new();
    private bool _preferencesWritable = true;
    private bool _keepHistory;
    private bool _settingsOpen;
    private int _settingsSection;
    private Task<string?>? _settingsSave;
    private bool _processActionPending;
    private Task<string?[]>? _termination;
    private ProcessSelection _selection = new(MacFrameSource.IdentityPolicy);
    private volatile Func<ProcessUsage, bool>? _retain;
    private bool _refreshingRows;
    private string _operationStatus = "";
    private string _networkChoicesKey = "";
    private bool _refreshingAdapters;
    private readonly ProcessPins _pins = new(MacFrameSource.IdentityPolicy);
    private readonly HistoryInspection _inspection = new();
    private int RangeSeconds => HistoryRange.SelectedIndex switch { 1 => 600, 2 => 1800, 3 => 3600, _ => 120 };
    private bool Grouped => GroupProcesses.IsChecked == true;

    public MainWindow()
    {
        InitializeComponent();
        _historyOrigins = new HistoryProvenance(_bootId);
        MachineLabel.Text = $"{Environment.MachineName} · {Environment.ProcessorCount} logical cores";
        _session = new MonitorSession(() => _source,
            () => new SamplingOptions(Retain: _retain),
            action => Dispatcher.UIThread.Post(action), identityPolicy: MacFrameSource.IdentityPolicy);
        _session.Recorded += Update;
        PropertyChanged += (_, change) =>
        {
            if ((change.Property == WindowStateProperty || change.Property == IsVisibleProperty) &&
                IsVisible && WindowState != WindowState.Minimized) Update();
        };
        _reportWriter = new CaptureReportWriter((record, error) => Dispatcher.UIThread.Post(() =>
        {
            if (_closing) return;
            var index = _captures.FindIndex(capture => capture.Id == record.Id);
            if (index >= 0) _captures[index] = record with { State = error is null ? "Saved" : "Failed" };
            if (error is not null) _operationStatus = $"Capture report failed: {error}";
            RefreshCaptures();
        }));
        _session.LiveSampleRecorded += _gaugeHistory.Observe;
        ResourceCards.SizeChanged += (_, _) => ResourceCards.Columns = ResourceCards.Bounds.Width < 840 ? 2 : 4;
        _session.LiveSampleRecorded += ObserveCaptures;
        _session.LiveSampleRecorded += JobsView.ObserveLiveSample;
        OpenSettings.Click += async (_, _) => await ShowSettings();
        ConfigureCaptures.Click += async (_, _) => await ShowSettings(3);
        KeyDown += async (_, args) =>
        {
            if (args.Key == Key.OemComma && args.KeyModifiers == KeyModifiers.Meta)
            { args.Handled = true; await ShowSettings(); }
        };
        CapturesList.SelectionChanged += (_, _) =>
        {
            if (_refreshingCaptures) return;
            if (CapturesList.SelectedItem is MacCaptureRecord record) SelectCapture(record);
        };
        OpenCaptureReport.Click += (_, _) => { if (CapturesList.SelectedItem is MacCaptureRecord record) OpenReport(record); };
        CopyCapture.Click += async (_, _) => await CopySelectedCapture();
        UsageSearch.TextChanged += (_, _) => UpdateUsage();
        UsageSortBox.SelectionChanged += (_, _) => UpdateUsage();
        ResetUsage.Click += (_, _) => { _session.Usage.Reset(DateTimeOffset.Now); UpdateUsage(); };
        ExportUsage.Click += async (_, _) => await Export(CsvExportKind.UsageTotals);
        DataTabs.SelectionChanged += (_, args) => { if (args.Source == DataTabs) { UpdateProcesses(); UpdateUsage(); JobsView.SetActive(DataTabs.SelectedIndex == 2); } };
        NetworkAdapter.ItemsSource = new[] { new MacNetworkChoice("", "Automatic · active Ethernet/Wi-Fi") };
        NetworkAdapter.SelectedIndex = 0;
        NetworkAdapter.SelectionChanged += (_, _) =>
        {
            if (!_refreshingAdapters && NetworkAdapter.SelectedItem is MacNetworkChoice choice)
                _source.NetworkInterfaceId = choice.Id.Length == 0 ? null : choice.Id;
        };
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
            if (selected is null || !_historyOrigins.CanPin(TrailHistory().LastOrDefault(), Grouped, _inspection.IsHistorical)) return;
            _pins.Toggle(selected.Usage, Grouped);
            UpdateRetention();
            UpdateProcesses();
        };
        ActivityChart.CaptureSelected += SelectCapture;
        Chart.CaptureSelected += SelectCapture;
        ActivityChart.SampleSelected += InspectSample;
        Chart.SampleSelected += InspectSample;
        CaptureMode.Click += (_, _) => { if (_session.Latest is { } sample) InspectSample(sample); };
        LiveMode.Click += (_, _) =>
        {
            if (!_historyOrigins.SameBoot(TrailHistory().LastOrDefault(), _session.LatestFull)) ClearSelection();
            _inspection.Live(); Update();
        };
        HistoryRange.SelectionChanged += (_, _) =>
        {
            _session.History.Resize(RangeSeconds);
            Chart.RangeSeconds = ActivityChart.RangeSeconds = RangeSeconds;
            Update();
        };
        ExportResources.Click += async (_, _) => await Export(CsvExportKind.ResourceHistory);
        ExportProcesses.Click += async (_, _) => await Export(CsvExportKind.ProcessSamples);
        AlwaysOnTop.IsCheckedChanged += (_, _) => Topmost = AlwaysOnTop.IsChecked == true;
        ShowProperties.Click += async (_, _) => await ShowProcessDetails();
        CopyProcessDetails.Click += async (_, _) => await CopyProcess();
        RevealExecutable.Click += (_, _) => RevealSelectedExecutable();
        EndProcess.Click += async (_, _) => await EndSelectedProcesses();
        ApplyPreferences();
        Opened += (_, _) => _startup = StartAsync();
        Closing += OnClosing;
    }

    private readonly ResourceGaugeHistory _gaugeHistory = new();

    private void Update()
    {
        if (_session is null || !IsVisible || WindowState == WindowState.Minimized) return;
        var sample = _session.Latest;
        if (sample is null)
        {
            ClearGauges();
            DiskValue.Text = "—";
            DiskTotal.Text = "Capacity unavailable";
            StatusLabel.Text = "Readings unavailable · retrying…";
            _refreshingRows = true;
            ProcessList.ItemsSource = Array.Empty<ProcessRow>();
            _refreshingRows = false;
            EmptyProcessLabel.IsVisible = true;
            EmptyProcessLabel.Text = "Process readings unavailable · retrying…";
            SelectionLabel.Text = "Selected process readings unavailable.";
            return;
        }

        var readings = _gaugeHistory.Readings;
        CpuGauge.Update(readings[0], sample.Resources.CpuKernelPercent is { } kernel ? $"Machine capacity · {kernel:F1}% system" : "Machine capacity · baseline pending");
        MemoryGauge.Update(readings[1], $"{GiB(sample.Resources.UsedMemoryBytes)} / {GiB(sample.Resources.TotalMemoryBytes)} physical\nPressure: {_source.Memory.Pressure} · compressed {GiB(_source.Memory.CompressedBytes)}");
        NetworkGauge.Update(readings[2], $"{sample.Resources.NetworkName} · receive + send\n↓ {Rate(sample.Resources.ReceiveBytesPerSecond)} · ↑ {Rate(sample.Resources.SendBytesPerSecond)}");
        StorageGauge.Update(readings[3], $"All block drivers · read + write\nR {Rate(sample.Resources.SecondaryDiskReadBytesPerSecond)} · W {Rate(sample.Resources.SecondaryDiskWriteBytesPerSecond)}");
        DiskValue.Text = GiB(sample.Resources.DiskFreeBytes);
        DiskTotal.Text = $"of {GiB(sample.Resources.DiskTotalBytes)}";
        UpdateActivity(sample);
        var display = TrailHistory();
        var captured = display.LastOrDefault();
        Chart.SelectedTimestamp = _inspection.IsHistorical ? captured?.MonotonicTimestamp : null;
        ActivityChart.SelectedTimestamp = Chart.SelectedTimestamp;
        Chart.Update(_session.History.Samples);
        ActivityChart.Update(_session.History.Samples);
        ActivityScale.Text = $"Network · storage · max {Rate(ActivityChart.ActivityMaximum)}";
        CaptureStatus.Text = !_inspection.IsHistorical ? $"Live · recording · {_session.History.Samples.Count} retained samples" : captured is null
            ? "Capture expired from retained history · return to Live or select another moment"
            : $"Captured {captured.Timestamp:yyyy-MM-dd HH:mm:ss zzz} · CPU {captured.TotalCpuPercent?.ToString("F1") ?? "—"}% · memory {GiB(captured.Resources.UsedMemoryBytes)} · network ↓ {Rate(captured.Resources.ReceiveBytesPerSecond)} ↑ {Rate(captured.Resources.SendBytesPerSecond)} · retained process observations";
        UpdateProcesses();
        UpdateUsage();
        StatusLabel.Text = _session.Error is null
            ? $"Updated {sample.Timestamp:HH:mm:ss} · macOS {Environment.OSVersion.Version}"
            : "Some readings are unavailable · retrying…";
        if (!_session.HasLiveSample)
        {
            ClearGauges();
            DiskValue.Text = "—";
            StatusLabel.Text = "Restored history · waiting for live readings…";
        }
        if (_operationStatus.Length > 0) StatusLabel.Text += $" · {_operationStatus}";
    }

    private void ClearGauges()
    {
        foreach (var gauge in new[] { CpuGauge, MemoryGauge, NetworkGauge, StorageGauge })
            gauge.Update(GaugeReading.Empty, "Waiting for live readings…");
    }

    private void UpdateActivity(MonitorSample sample)
    {
        var interfaces = _source.NetworkInterfaces;
        var key = string.Join("\n", interfaces.Select(adapter => adapter.Id + adapter.Name));
        if (_networkChoicesKey == key) return;
        _networkChoicesKey = key;
        var selected = _source.NetworkInterfaceId ?? "";
        var choices = new[] { new MacNetworkChoice("", "Automatic · active Ethernet/Wi-Fi") }.Concat(interfaces).ToList();
        if (!choices.Any(choice => choice.Id == selected)) choices.Add(new(selected, $"{selected} · disconnected"));
        _refreshingAdapters = true;
        NetworkAdapter.ItemsSource = choices;
        NetworkAdapter.SelectedItem = choices.First(choice => choice.Id == selected);
        _refreshingAdapters = false;
    }

    private static string Rate(double? value) => value is { } bytes
        ? bytes >= 1048576 ? $"{bytes / 1048576:F1} MiB/s" : $"{bytes / 1024:F1} KiB/s" : "—";

    private ProcessSort Sort => ProcessSortBox.SelectedIndex switch
    {
        1 => ProcessSort.Memory,
        2 => ProcessSort.Threads,
        3 => ProcessSort.Io,
        _ => ProcessSort.Cpu
    };

    private void UpdateProcesses()
    {
        if (_session is null || DataTabs.SelectedIndex != 0) return;
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
            ProcessActionsPanel.IsVisible = false;
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
        ProcessActionsPanel.IsVisible = selected is not null;
        RevealExecutable.IsEnabled = selected is not null && selected.Usage.ImagePath.Length > 0;
        EndProcess.Content = Grouped ? "End group…" : "End process…";
        EndProcess.IsEnabled = !_processActionPending && !_inspection.IsHistorical && selected?.State == PinnedProcessState.Present &&
            selected.Usage.ProcessId > 1 && (Grouped
                ? selected.Usage.MemberIds.Count > 0 && selected.Usage.MemberIds.All(pid => pid > 1 && pid != Environment.ProcessId)
                : selected.Usage.ProcessId != Environment.ProcessId);
        PinSelection.IsEnabled = selected is not null && _historyOrigins.CanPin(TrailHistory().LastOrDefault(), Grouped, _inspection.IsHistorical);
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
        return !_inspection.IsHistorical && !_session.HasLiveSample ? [] :
            _inspection.DisplayHistory(_session.History.Samples, _session.LatestFull);
    }

    private IReadOnlyList<MonitorSample> LiveHistory()
    {
        return TrailHistory().TakeLast(ProcessRanking.DefaultWindowSeconds + 1).ToArray();
    }

    private async Task Export(CsvExportKind kind)
    {
        var samples = HistoryInspection.VisibleSamples(_session.History.Samples, RangeSeconds).ToArray();
        var apps = _session.Usage.Apps.ToArray();
        var since = _session.Usage.Since;
        var machine = _session.Usage.MachineBusyTicks;
        var notAttributed = _session.Usage.NotAttributedCpuTicks;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = kind switch { CsvExportKind.ResourceHistory => "Export resource history", CsvExportKind.UsageTotals => "Export session usage totals", _ => "Export retained process samples" },
            SuggestedFileName = $"MacMon-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (path is null) { _operationStatus = "Choose a local file for export."; return; }
        try
        {
            await Task.Run(() => CsvExport.Write(path, kind, samples, apps, since, machine, notAttributed));
            _operationStatus = kind == CsvExportKind.UsageTotals ? $"Exported session usage to {path}" : $"Exported {samples.Length} retained samples to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _operationStatus = $"Export failed: {ex.Message}"; }
    }

    private static string GiB(ulong? bytes) => bytes is { } value ? $"{value / 1073741824d:F1} GiB" : "—";

    private void UpdateUsage()
    {
        if (_session is null || DataTabs.SelectedIndex != 1) return;
        UsageList.ItemsSource = UsagePresentation.Rows(_session.Usage, UsageSearch.Text ?? "", (UsageSort)Math.Max(0, UsageSortBox.SelectedIndex));
        UsageStatus.Text = $"Live totals since {_session.Usage.Since:yyyy-MM-dd HH:mm:ss zzz} · un-attributed CPU {TimeSpan.FromTicks(_session.Usage.NotAttributedCpuTicks).TotalSeconds:F1} s\n" +
            "Includes exited apps. Busy share uses observed machine CPU time. Missing intervals are excluded; disk I/O excludes network. Capture/chart range does not change these totals.";
    }

    private void ObserveCaptures(MonitorSample sample)
    {
        if (!_captureOptions.Enabled) return;
        var firedCaptures = _captureRules.Observe(sample);
        if (firedCaptures.Count == 0) return;
        foreach (var fired in firedCaptures)
        {
            var record = MacCaptureReports.Create(fired, Path.Combine(MacPaths.AppSupport, "Captures"));
            if (!_reportWriter.Enqueue(record, MacCaptureReports.AutomaticText(fired)))
            {
                record = record with { State = "Failed" };
                _operationStatus = "Capture report queue is full; report could not be saved.";
            }
            _captures.Add(record);
            if (_captures.Count > 200) _captures.RemoveAt(0);
        }
        RefreshCaptures();
    }

    private void RefreshCaptures()
    {
        var selected = (CapturesList.SelectedItem as MacCaptureRecord)?.Id;
        _refreshingCaptures = true;
        CapturesList.ItemsSource = _captures.AsEnumerable().Reverse().ToArray();
        CapturesList.SelectedItem = _captures.FirstOrDefault(record => record.Id == selected);
        _refreshingCaptures = false;
        OpenCaptureReport.IsEnabled = CapturesList.SelectedItem is MacCaptureRecord { State: "Saved" };
        Chart.Captures = ActivityChart.Captures = _captures.ToArray();
    }

    private void SelectCapture(MacCaptureRecord record)
    {
        var sample = _session.History.Samples.FirstOrDefault(record.Peak.Matches);
        if (sample is not null) { DataTabs.SelectedIndex = 0; InspectSample(sample); }
        else OpenReport(record);
        OpenCaptureReport.IsEnabled = record.State == "Saved";
    }

    private void OpenReport(MacCaptureRecord record)
    {
        if (!File.Exists(record.ReportPath)) { _operationStatus = "Capture report is unavailable or still saving."; return; }
        try { Process.Start(new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, ArgumentList = { record.ReportPath } }); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        { _operationStatus = $"Could not open report: {ex.Message}"; }
    }

    private async Task CopySelectedCapture()
    {
        var sample = TrailHistory().LastOrDefault();
        if (sample is null) { _operationStatus = "Selected capture has expired."; return; }
        var rows = ProcessPresentation.Rows(TrailHistory(), sample, Grouped, Sort, ProcessSearchBox.Text ?? "",
            _pins, PinnedOnly.IsChecked == true, _inspection.IsHistorical).Select(row => row.Process).ToArray();
        var note = $"Displayed {(Grouped ? "grouped" : "individual")} rows: CPU and disk I/O averaged over five observed seconds; resident and threads are latest observations. Filter: {ProcessSearchBox.Text}";
        var text = MacCaptureReports.Text(sample, rows, note);
        if (Clipboard is { } clipboard) { await clipboard.SetTextAsync(text); _operationStatus = "Capture copied."; }
    }

    private (PinnedProcess? Selected, MonitorSample? Sample) SelectedProcess() =>
        (_selection.Resolve(LiveHistory(), Grouped, _inspection.IsHistorical, Sort), TrailHistory().LastOrDefault());

    private async Task ShowProcessDetails()
    {
        var (selected, sample) = SelectedProcess();
        if (selected is null || sample is null) return;
        await new ProcessDialog("Process properties", ProcessDetails.Text(selected, sample, Grouped, _inspection.IsHistorical)).ShowDialog<bool>(this);
    }

    private async Task CopyProcess()
    {
        var (selected, sample) = SelectedProcess();
        if (selected is null || sample is null || Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(ProcessDetails.Text(selected, sample, Grouped, _inspection.IsHistorical));
        _operationStatus = "Process details copied.";
    }

    private void RevealSelectedExecutable()
    {
        var (selected, _) = SelectedProcess();
        if (selected is null || !File.Exists(selected.Usage.ImagePath)) { _operationStatus = "Executable path is unavailable."; return; }
        try { using var reveal = Process.Start(new ProcessStartInfo("/usr/bin/open")
            { UseShellExecute = false, ArgumentList = { "-R", selected.Usage.ImagePath } }); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        { _operationStatus = $"Could not reveal executable: {ex.Message}"; }
    }

    private async Task EndSelectedProcesses()
    {
        var (selected, _) = SelectedProcess();
        if (_processActionPending || _inspection.IsHistorical || selected?.State != PinnedProcessState.Present || _session.LatestFull is not { } latest) return;
        var usage = selected.Usage;
        var policy = MacFrameSource.IdentityPolicy;
        var members = latest.Processes.Where(process => Grouped
            ? policy.Comparer.Equals(policy.ExecutableKey(process), policy.ExecutableKey(usage))
            : process.Identity == usage.Identity).ToArray();
        if (members.Length == 0) { _operationStatus = "The selected process is no longer observed."; return; }
        var actions = new MacProcessActions();
        var targets = members.Select(actions.Prepare).ToArray();
        if (targets.Any(target => target is null))
        { _operationStatus = "Could not prepare every target. System, other-user, MacMon, changed or inaccessible processes are protected."; return; }
        var details = "Request SIGTERM for exactly these observed process lifetimes? Unsaved work can be lost.\n" +
            "Processes started after this dialog opens are excluded. macOS may deny the request, and a process may ignore SIGTERM.\n\n" +
            string.Join("\n", targets.Select(target => $"{target!.Process.Name} · PID {target.Process.ProcessId}\n{target.Process.ImagePath}"));
        _processActionPending = true;
        try
        {
            if (!await new ProcessDialog(Grouped ? "End executable group?" : "End process?", details, true).ShowDialog<bool>(this)) return;
            _termination = Task.Run(() => targets.Select(target => actions.Terminate(target!)).ToArray());
            var results = await _termination;
            var errors = results.Where(error => error is not null).ToArray();
            _operationStatus = $"Termination requested for {results.Length - errors.Length} of {results.Length} captured targets." +
                (errors.Length > 0 ? $" {errors[0]}" : "");
        }
        finally { _processActionPending = false; if (!_closing) UpdateSelection(); }
    }

    private async Task ShowSettings(int? section = null)
    {
        if (_settingsOpen || _closing) return;
        _settingsOpen = true;
        var networks = new[] { new MacNetworkChoice("", "Automatic · active Ethernet/Wi-Fi") }
            .Concat(_source.NetworkInterfaces).ToList();
        if (_source.NetworkInterfaceId is { Length: > 0 } id && networks.All(choice => choice.Id != id))
            networks.Add(new(id, $"{id} · disconnected"));
        var window = new SettingsWindow(PreferencesSnapshot(), networks, section ?? _settingsSection,
            _preferencesWritable, SaveSettings);
        try { await window.ShowDialog(this); }
        finally { _settingsSection = window.Section; _settingsOpen = false; }
    }

    private Task<string?> SaveSettings(SettingsValues values) => _settingsSave = SaveSettingsCore(values);

    private async Task<string?> SaveSettingsCore(SettingsValues values)
    {
        if (!_preferencesWritable) return "The existing settings file is protected from replacement.";
        var snapshot = values.ApplyTo(PreferencesSnapshot(), _bootId);
        try { await Task.Run(() => MacPreferenceStore.Save(_preferencesPath, snapshot)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return $"Settings could not be saved: {ex.Message}"; }
        _preferences = snapshot;
        AlwaysOnTop.IsChecked = snapshot.AlwaysOnTop;
        _keepHistory = snapshot.KeepHistory;
        HistoryRange.SelectedIndex = snapshot.HistoryRange;
        _source.NetworkInterfaceId = snapshot.NetworkId;
        _networkChoicesKey = "\0"; // Rebuild the interface choice even if the discovered set did not change.
        if (_captureOptions.Enabled != snapshot.Captures.Enabled ||
            _captureOptions.CooldownSeconds != snapshot.Captures.CooldownSeconds ||
            !_captureOptions.Rules.SequenceEqual(snapshot.Captures.Rules))
        {
            _captureOptions = snapshot.Captures;
            _captureRules = new(_captureOptions.Rules, TimeSpan.FromSeconds(_captureOptions.CooldownSeconds), Environment.ProcessorCount);
        }
        (Application.Current as App)?.ApplyAppearance(snapshot.Theme, snapshot.Palette, snapshot.GaugeStyle);
        _operationStatus = "Settings saved";
        Update();
        return null;
    }

    private void ApplyPreferences()
    {
        var loaded = MacPreferenceStore.Load(_preferencesPath, _bootId);
        _preferences = loaded.Preferences; _preferencesWritable = loaded.CanSave;
        (Application.Current as App)?.ApplyAppearance(_preferences.Theme, _preferences.Palette, _preferences.GaugeStyle);
        if (loaded.Error is not null) _operationStatus = loaded.Error;
        Width = _preferences.Width; Height = _preferences.Height;
        if (Screens.Primary is { } screen)
        {
            Width = Math.Min(Width, Math.Max(MinWidth, screen.WorkingArea.Width / screen.Scaling - 40));
            Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 60));
        }
        MainLayout.RowDefinitions[1].Height = new GridLength(_preferences.DashboardRatio, GridUnitType.Star);
        MainLayout.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
        _pins.Restore(_preferences.Pins);
        GroupProcesses.IsChecked = _preferences.Grouped; PinnedOnly.IsChecked = _preferences.PinnedOnly;
        ProcessSortBox.SelectedIndex = _preferences.ProcessSort; UsageSortBox.SelectedIndex = _preferences.UsageSort;
        JobsView.SortIndex = _preferences.JobSort; JobsView.FilterIndex = _preferences.JobFilter;
        DataTabs.SelectedIndex = _preferences.DataTab; HistoryRange.SelectedIndex = _preferences.HistoryRange;
        AlwaysOnTop.IsChecked = _preferences.AlwaysOnTop;
        _keepHistory = _preferences.KeepHistory;
        _session.History.Resize(RangeSeconds);
        _source.NetworkInterfaceId = _preferences.NetworkId;
        _captureOptions = _preferences.Captures;
        _captureRules = new(_captureOptions.Rules, TimeSpan.FromSeconds(_captureOptions.CooldownSeconds), Environment.ProcessorCount);
        UpdateRetention();
    }

    private MacPreferences PreferencesSnapshot() => _preferences with
    {
        BootId = _bootId, Grouped = Grouped, PinnedOnly = PinnedOnly.IsChecked == true,
        Pins = _pins.Snapshot(), ProcessSort = ProcessSortBox.SelectedIndex, UsageSort = UsageSortBox.SelectedIndex,
        DataTab = DataTabs.SelectedIndex, JobSort = JobsView.SortIndex, JobFilter = JobsView.FilterIndex, HistoryRange = HistoryRange.SelectedIndex, NetworkId = _source.NetworkInterfaceId,
        AlwaysOnTop = Topmost, Captures = _captureOptions, KeepHistory = _keepHistory,
        Width = WindowState == WindowState.Normal ? Width : _preferences.Width,
        Height = WindowState == WindowState.Normal ? Height : _preferences.Height,
        DashboardRatio = MainLayout.RowDefinitions[1].Height.Value / Math.Max(0.01, MainLayout.RowDefinitions[3].Height.Value)
    };

    private async Task StartAsync()
    {
        var keep = _keepHistory;
        var range = RangeSeconds;
        var restored = await Task.Run(() => keep
            ? MacHistoryStore.Load(_historyPath, range, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()) : new HistoryLoadResult([]));
        var reports = await Task.Run(() => MacCaptureReports.LoadRecords(Path.Combine(MacPaths.AppSupport, "Captures")));
        if (_closing) return;
        _historyWritable = restored.CanSave;
        _historyOrigins.Restore(restored.Entries);
        _session.SetHistoryClockOffset(restored.ClockOffset);
        // Restore the bounded history as a batch; redraw once instead of rebuilding every intermediate view.
        _session.Recorded -= Update;
        try { foreach (var entry in restored.Entries) _session.Record(entry.Sample); }
        finally { _session.Recorded += Update; }
        if (restored.Entries.LastOrDefault() is { } latest)
        {
            _inspection.Select(latest.Sample);
            _operationStatus = $"Restored {restored.Entries.Count} history samples; Usage starts fresh.";
        }
        if (restored.Error is not null) _operationStatus = restored.Error;
        _captures.AddRange(reports.Select(record => record with { Peak = restored.ResolvePeak(record.Peak) }));
        RefreshCaptures();
        Update();
        _sampling = _session.Run(_shutdown.Token);
    }

    private void ClearSelection()
    {
        _selection = new ProcessSelection(MacFrameSource.IdentityPolicy);
        UpdateRetention();
    }

    private void InspectSample(MonitorSample sample)
    {
        if (!_historyOrigins.SameBoot(TrailHistory().LastOrDefault(), sample)) ClearSelection();
        _inspection.Select(sample); Update();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_shutdownComplete) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        _session.Close();
        _session.Recorded -= Update;
        _session.LiveSampleRecorded -= _gaugeHistory.Observe;
        _session.LiveSampleRecorded -= ObserveCaptures;
        _session.LiveSampleRecorded -= JobsView.ObserveLiveSample;
        await JobsView.Stop();
        await _shutdown.CancelAsync();
        if (_startup is not null) await _startup;
        if (_sampling is not null) await _sampling;
        await _reportWriter.Complete();
        if (_termination is not null) await _termination;
        if (_settingsSave is not null) await _settingsSave;
        if (_preferencesWritable)
        {
            var snapshot = PreferencesSnapshot().Normalized(_bootId);
            var history = _historyOrigins.Snapshot(_session.History.Samples);
            try
            {
                await Task.Run(() =>
                {
                    if (_historyWritable)
                    {
                        if (snapshot.KeepHistory) MacHistoryStore.Save(_historyPath, history);
                        else if (File.Exists(_historyPath)) File.Delete(_historyPath);
                    }
                    MacPreferenceStore.Save(_preferencesPath, snapshot);
                });
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            { await new ProcessDialog("Settings or history could not be saved", $"{ex.Message}\nThe app will close without saving these settings.").ShowDialog<bool>(this); }
        }
        _shutdown.Dispose();
        _shutdownComplete = true;
        Close();
    }
}
