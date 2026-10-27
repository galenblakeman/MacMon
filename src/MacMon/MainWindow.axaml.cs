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
    private readonly ProcessPins _pins = new(MacFrameSource.IdentityPolicy);
    private readonly HistoryInspection _inspection = new();
    private int RangeSeconds => _historyRange switch { 1 => 600, 2 => 1800, 3 => 3600, _ => 120 };
    private bool Grouped => GroupProcesses.IsChecked == true;

    private WindowViewState _viewState = new(new MacPreferences());
    private bool _switchingView;
    private bool CompactMode => _viewState.CompactMode;

    public MainWindow()
    {
        InitializeComponent();
        UpdateTitleBarInsets();
        _historyOrigins = new HistoryProvenance(_bootId);
        VersionLabel.Text = typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        OpenInfo.Click += async (_, _) => await ShowHardwareInfo();
        OpenExport.Click += async (_, _) => await ShowExportDialog();
        PinSelection.Click += (_, _) => MoreProcessActions.Flyout?.Hide();
        RevealExecutable.Click += (_, _) => MoreProcessActions.Flyout?.Hide();
        ShowUsageActions.IsCheckedChanged += (_, _) => ButtonIcon.SetKind(ShowUsageActions, ShowUsageActions.IsChecked == true ? "ChevronDown" : "ChevronUp");
        MacDesktopIntegration.Activated += ShowFromShortcut;
        _session = new MonitorSession(() => _source,
            () => new SamplingOptions(Retain: _retain),
            action => Dispatcher.UIThread.Post(action), identityPolicy: MacFrameSource.IdentityPolicy);
        InitializeResourceDetails();
        _session.Recorded += Update;
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsExtendedIntoWindowDecorationsProperty) UpdateTitleBarInsets();
            if ((change.Property == WindowStateProperty || change.Property == IsVisibleProperty) &&
                IsVisible && WindowState != WindowState.Minimized) Update();
            if (change.Property == WindowStateProperty && WindowState == WindowState.Minimized) HideResourceDetails();
        };
        _reportWriter = new CaptureReportWriter((record, error) => Dispatcher.UIThread.Post(() =>
        {
            if (_closing) return;
            var index = _captures.FindIndex(capture => capture.Id == record.Id);
            if (index >= 0) _captures[index] = record with { State = error is null ? "Saved" : "Failed" };
            if (error is not null) _operationStatus = $"Capture report failed: {error}";
            else if (_captureOptions.Notify && !_closing) MacDesktopIntegration.Notify(UiText.Get("Automatic capture saved"), record.Label);
            RefreshCaptures();
        }));
        _session.LiveSampleRecorded += _gaugeHistory.Observe;
        ResourceCards.Columns = 5;
        _session.LiveSampleRecorded += ObserveCaptures;
        _session.LiveSampleRecorded += JobsView.ObserveLiveSample;
        JobsView.SummaryChanged += summary => { JobSummary.Text = summary; ToolTip.SetTip(JobSummary, JobsView.SummaryDetails); };
        EnterCompactView.Click += (_, _) => SetCompactView(true);
        CompactPanel.InfoRequested += async () => await ShowHardwareInfo();
        CompactPanel.ExportRequested += async () => await ShowExportDialog();
        CompactPanel.FullViewRequested += () => SetCompactView(false);
        CompactPanel.SettingsRequested += async () => await ShowSettings();
        CompactPanel.TopmostChanged += value => AlwaysOnTop.IsChecked = value;
        SizeChanged += (_, args) =>
        {
            if (!_switchingView && IsVisible) _viewState.ObserveSize(args.NewSize, WindowState == WindowState.Normal);
        };
        OpenSettings.Click += async (_, _) => await ShowSettings();
        ConfigureCaptures.Click += async (_, _) => { AutomaticCaptures.Flyout?.Hide(); await ShowSettings(3); };
        KeyDown += async (_, args) =>
        {
            if (args.Key == Key.M && args.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Shift))
            { args.Handled = true; SetCompactView(!CompactMode); }
            else if (args.Key == Key.Escape && CompactMode)
            { args.Handled = true; SetCompactView(false); }
            else if (args.Key == Key.OemComma && args.KeyModifiers == KeyModifiers.Meta)
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

        ResetUsage.Click += (_, _) => { _session.Usage.Reset(DateTimeOffset.Now); UpdateUsage(); };
        DataTabs.SelectionChanged += (_, args) => { if (args.Source == DataTabs) { UpdateTabSummary(); UpdateProcesses(); UpdateUsage(); JobsView.SetActive(!CompactMode && DataTabs.SelectedIndex == 1); } };
        ProcessSearchBox.TextChanged += (_, _) => UpdateProcesses();

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
        InitializeCharts();
        InitializeTables();
        Chart.CaptureSelected += SelectCapture;

        Chart.SampleSelected += InspectSample;
        CaptureMode.Click += (_, _) => { if (_session.Latest is { } sample) InspectSample(sample); };
        LiveMode.Click += (_, _) => ReturnToLive();
        AlwaysOnTop.IsCheckedChanged += (_, _) => { Topmost = AlwaysOnTop.IsChecked == true; CompactPanel.AlwaysOnTop = Topmost; };
        ShowProperties.Click += async (_, _) => await ShowProcessDetails();
        CopyProcessDetails.Click += async (_, _) => await CopyProcess();
        RevealExecutable.Click += (_, _) => RevealSelectedExecutable();
        EndProcess.Click += async (_, _) => await EndSelectedProcesses();
        ApplyPreferences();
        Opened += (_, _) => { FitWindowToScreen(); _startup = StartAsync(); };
        Closing += OnClosing;
    }

    private readonly ResourceGaugeHistory _gaugeHistory = new();

    private void Update()
    {
        if (_session is null || !IsVisible || WindowState == WindowState.Minimized) return;
        var sample = _session.Latest;
        if (CompactMode)
        {
            if (sample is not null && _session.HasLiveSample) UpdateResourceGauges(sample);
            else ClearGauges();
            CompactPanel.Status = sample is null ? "Readings unavailable · retrying…" : !_session.HasLiveSample
                ? "Restored history · waiting for live readings…" : _session.Error is not null
                ? "Some readings are unavailable · retrying…" : $"Live · updated {sample.Timestamp:HH:mm:ss} · peak markers show the last 2 minutes";
            return;
        }
        if (sample is null)
        {
            ClearGauges();
            StatusLabel.Text = "Readings unavailable · retrying…";
            _refreshingRows = true;
            ProcessList.ItemsSource = Array.Empty<ProcessRow>();
            _refreshingRows = false;
            EmptyProcessLabel.IsVisible = true;
            EmptyProcessLabel.Text = "Process readings unavailable · retrying…";
            SelectionLabel.Text = "Selected process readings unavailable.";
            return;
        }

        UpdateResourceGauges(sample);
        var display = TrailHistory();
        var captured = display.LastOrDefault();
        JobsView.SetInspection(_inspection.IsHistorical ? captured : null, _inspection.IsHistorical);
        Chart.SelectedTimestamp = _inspection.IsHistorical ? captured?.MonotonicTimestamp : null;

        Chart.Update(_session.History.Samples);
        UpdateChartLabels();

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
            StatusLabel.Text = "Restored history · waiting for live readings…";
        }
        if (_operationStatus.Length > 0) StatusLabel.Text = _operationStatus;
        StatusLabel.IsVisible = _session.Error is not null || _operationStatus.Length > 0;
    }

    private ResourceGauge[] VisibleGauges => CompactMode ? CompactPanel.Gauges : [CpuGauge, MemoryGauge, NetworkGauge, StorageGauge, GpuGauge, SecondDiskGauge];
    private void UpdateResourceGauges(MonitorSample sample)
    {
        var r = sample.Resources;
        var usedMemory = (r.UsedMemoryBytes / 1073741824d)?.ToString("F1") ?? "—";
        string[] descriptions = [
            r.CpuClockMhz is > 0 ? $"{r.CpuClockMhz.Value / 1000:F1} GHz" : "Speed unavailable",
            $"{usedMemory} / {GiB(r.TotalMemoryBytes)}\nPressure: {_source.Memory.Pressure} · compressed {GiB(_source.Memory.CompressedBytes)}",
            $"{r.NetworkName} · download\n↑ {ResourceGaugeHistory.NetworkRate(r.SendBytesPerSecond)}",
            $"{r.DiskName} · read + write\n{Rate(ResourceCharts.DiskRate(r, false))}",
            r.GpuName.Length > 0 ? r.GpuName : "Device utilization unavailable",
            $"{r.SecondaryDiskName} · read + write\n{Rate(ResourceCharts.DiskRate(r, true))}"];
        var gauges = VisibleGauges;
        for (var index = 0; index < gauges.Length; index++) gauges[index].Update(_gaugeHistory.Readings[index], descriptions[index]);
    }

    private void ClearGauges()
    {
        foreach (var gauge in VisibleGauges)
            gauge.Update(GaugeReading.Empty, "Waiting for live readings…");
    }

    private static string Rate(double? value) => TableUnits.IoRate(value);

    private ProcessSort Sort => ProcessSortBox.SelectedIndex switch
    {
        1 => ProcessSort.Memory,
        2 => ProcessSort.Threads,
        3 => ProcessSort.Io,
        _ => ProcessSort.Cpu
    };

    private void UpdateProcesses()
    {
        if (_session is null || CompactMode || DataTabs.SelectedIndex != 0) return;
        var display = TrailHistory();
        if (display.LastOrDefault() is not { } sample)
        {
            _refreshingRows = true;
            ProcessList.ItemsSource = Array.Empty<ProcessRow>();
            _refreshingRows = false;
            EmptyProcessLabel.IsVisible = true;
            EmptyProcessLabel.Text = "Selected capture is no longer retained";
            ProcessSelectionName.Text = UiText.Get("Select a process"); SelectionIcon.IsVisible = false;
            PinSelection.IsEnabled = false;
            ProcessCountLabel.Text = "Capture expired";
            SelectionLabel.Text = "Return to Live or select another moment.";
            ProcessActionsPanel.IsVisible = false;
            return;
        }
        UpdateProcessHeaders();
        var rows = PresentedProcesses(sample);
        _refreshingRows = true;
        ProcessList.ItemsSource = rows;
        ProcessList.SelectedItem = rows.FirstOrDefault(row => _selection.Contains(row.Process, Grouped));
        _refreshingRows = false;
        EmptyProcessLabel.IsVisible = rows.Length == 0;
        EmptyProcessLabel.Text = _session.Error == SamplingError.ProcessesUnavailable
            ? "Process enumeration unavailable · retrying…"
            : sample.IsGap ? "Establishing process baselines…" : "No matching processes";
        ProcessCountLabel.Text = UiText.Format("Processes: {0} · Threads: {1}", sample.Resources.ProcessCount?.ToString() ?? "—", sample.Processes.Sum(p => (long)(p.ThreadCount ?? 0)));
        ToolTip.SetTip(ProcessCountLabel, $"{rows.Length} rows shown; {sample.UnavailableProcesses} processes unavailable. Thread count sums readable observations; handles are not collected on macOS.");
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = ResolveSelection();
        ProcessActionsPanel.IsVisible = selected is not null && ShowProcessActions.IsChecked == true;
        ProcessSelectionName.Text = selected?.Usage.Name ?? UiText.Get("Select a process");
        ToolTip.SetTip(ProcessSelectionName, selected?.Usage.Name);
        SelectionIcon.IsVisible = selected is not null;
        SelectionIcon.Source = selected is null ? null : ApplicationIcons.For(selected.Usage.ImagePath);
        ButtonIcon.SetKind(ShowProcessActions, ShowProcessActions.IsChecked == true ? "ChevronDown" : "ChevronUp");
        RevealExecutable.IsEnabled = selected is not null && selected.Usage.ImagePath.Length > 0;
        EndProcess.Content = Grouped ? "End group…" : "End process…";
        EndProcess.IsEnabled = !_processActionPending && !_inspection.IsHistorical && selected?.State == PinnedProcessState.Present &&
            selected.Usage.ProcessId > 1 && (Grouped
                ? selected.Usage.MemberIds.Count > 0 && selected.Usage.MemberIds.All(pid => pid > 1 && pid != Environment.ProcessId)
                : selected.Usage.ProcessId != Environment.ProcessId);
        ToolTip.SetTip(SelectionLabel, selected?.Usage.ImagePath);
        PinSelection.IsEnabled = selected is not null && _historyOrigins.CanPin(TrailHistory().LastOrDefault(), Grouped, _inspection.IsHistorical);
        PinSelection.Content = selected is not null && _pins.Contains(selected.Usage, Grouped) ? "Unpin selected" : "Pin selected";
        SelectionLabel.Text = selected is null ? ""
            : selected.State != PinnedProcessState.Present
                ? $"PID {selected.Usage.ProcessId} · no longer observed or unavailable"
                : $"PID {selected.Usage.ProcessId} · started " +
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

    private async Task Export(CsvExportKind kind, bool chartRange = true)
    {
        var samples = (chartRange ? HistoryInspection.VisibleSamples(_session.History.Samples, RangeSeconds) : _session.History.Samples).ToArray();
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
        if (_session is null || CompactMode || DataTabs.SelectedIndex != 2) return;
        UpdateUsageHeaders();
        var rows = UsagePresentation.Rows(_session.Usage, UsageSearch.Text ?? "", (UsageSort)Math.Max(0, UsageSortBox.SelectedIndex),
            SelectedLimit(UsageLimitBox), UsageAppsOnly.IsChecked == true, _usageAscending, _session.Latest?.Timestamp);
        _refreshingUsage = true;
        try
        {
            UsageList.ItemsSource = rows;
            UsageList.SelectedItem = rows.FirstOrDefault(row => row.App.Key == _usageSelection);
        }
        finally { _refreshingUsage = false; }
        UpdateUsageSelection();
        var attribution = new UsageAttribution(_session.Usage.NotAttributedCpuTicks, _session.Usage.MachineBusyTicks);
        UnattributedCpu.Text = attribution.Cpu; UnattributedShare.Text = attribution.Share;
        UsageStatus.Text = $"{rows.Length} shown · live totals since {_session.Usage.Since:HH:mm:ss zzz}";
        ToolTip.SetTip(UsageStatus, "Includes exited apps. Busy share uses observed machine CPU time. Missing intervals are excluded; disk I/O excludes network. Capture/chart range does not change these totals.");
    }

    private void ObserveCaptures(MonitorSample sample)
    {
        if (!_captureOptions.Enabled) return;
        var firedCaptures = _captureRules.Observe(sample);
        if (firedCaptures.Count == 0) return;
        foreach (var fired in firedCaptures)
        {
            var record = MacCaptureReports.Create(fired, _captureOptions.ReportFolder);
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
        Chart.Captures = _captures.ToArray();
        CaptureToolbar.Update(AutomaticCaptures, _captureOptions.Enabled, _captures.Count);
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
        var rows = PresentedProcesses(sample).Select(row => row.Process).ToArray();
        var note = $"Displayed {(Grouped ? "grouped" : "individual")} rows: CPU and disk I/O averaged over five observed seconds; resident and threads are latest observations. Filter: {ProcessSearchBox.Text}";
        var text = MacCaptureReports.Text(sample, rows, note);
        if (Clipboard is { } clipboard) { await clipboard.SetTextAsync(text); _operationStatus = "Capture copied."; }
    }

    private PinnedProcess? ResolveSelection()
    {
        var history = LiveHistory();
        return history.Count == 0 ? null : _selection.Resolve(history, Grouped, _inspection.IsHistorical, Sort);
    }

    private (PinnedProcess? Selected, MonitorSample? Sample) SelectedProcess() =>
        (ResolveSelection(), TrailHistory().LastOrDefault());

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
        HideResourceDetails();
        _settingsOpen = true;
        var networks = new[] { new MacNetworkChoice("", "Automatic · active Ethernet/Wi-Fi") }
            .Concat(_source.NetworkInterfaces).ToList();
        if (_source.NetworkInterfaceId is { Length: > 0 } id && networks.All(choice => choice.Id != id))
            networks.Add(new(id, $"{id} · disconnected"));
        var window = new SettingsWindow(PreferencesSnapshot(), networks, section ?? _settingsSection,
            _preferencesWritable, SaveSettings, _source.DiskChoices);
        if (CompactMode) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { await window.ShowDialog(this); }
        finally { _settingsSection = window.Section; _settingsOpen = false; }
    }

    private Task<string?> SaveSettings(SettingsValues values) => _settingsSave = SaveSettingsCore(values);

    private async Task<string?> SaveSettingsCore(SettingsValues values)
    {
        if (!_preferencesWritable) return "The existing settings file is protected from replacement.";
        var snapshot = values.ApplyTo(PreferencesSnapshot(), _bootId);
        var nativeError = await SettingsEffects.Save(_preferences, snapshot, values.OpenAtLogin, new NativeDesktopSettingsServices(),
            () => Task.Run(() => MacPreferenceStore.Save(_preferencesPath, snapshot)));
        if (nativeError is not null) return UiText.Get(nativeError);
        var previousFolder = _captureOptions.ReportFolder;
        _preferences = snapshot;
        AlwaysOnTop.IsChecked = snapshot.AlwaysOnTop;
        _keepHistory = snapshot.KeepHistory;
        SetHistoryRange(snapshot.HistoryRange);
        _source.NetworkInterfaceId = snapshot.NetworkId;
        var previousSecondDisk = _source.SecondaryDiskId;
        _source.PrimaryDiskId = snapshot.PrimaryDiskId; _source.SecondaryDiskId = snapshot.SecondaryDiskId;
        if (snapshot.SecondaryDiskId is not null && snapshot.SecondaryDiskId != previousSecondDisk) SecondDiskGauge.SeriesToggle.IsChecked = true;
        UpdateDiskLayout();
        if (_captureOptions.Enabled != snapshot.Captures.Enabled ||
            _captureOptions.CooldownSeconds != snapshot.Captures.CooldownSeconds ||
            !_captureOptions.Rules.SequenceEqual(snapshot.Captures.Rules))
        {
            _captureRules = new(snapshot.Captures.Rules, TimeSpan.FromSeconds(snapshot.Captures.CooldownSeconds), Environment.ProcessorCount);
        }
        _captureOptions = snapshot.Captures;
        if (previousFolder != _captureOptions.ReportFolder)
        {
            var reports = await Task.Run(() => MacCaptureReports.LoadRecords(_captureOptions.ReportFolder));
            if (!_closing)
            {
                var known = _captures.Select(r => r.Id).ToHashSet(); _captures.AddRange(reports.Where(r => !known.Contains(r.Id)));
                _captures.Sort((a, b) => a.Peak.Timestamp.CompareTo(b.Peak.Timestamp));
                if (_captures.Count > 200) _captures.RemoveRange(0, _captures.Count - 200);
                RefreshCaptures();
            }
        }
        UiText.Apply(snapshot.Language);
        CaptureToolbar.Update(AutomaticCaptures, _captureOptions.Enabled, _captures.Count);
        (Application.Current as App)?.ApplyAppearance(snapshot.Theme, snapshot.Palette, snapshot.GaugeStyle);
        _operationStatus = UiText.Get(MacDesktopIntegration.LoginStatus == 2 ? "Settings saved · approve Open at login in Login Items." : "Settings saved");
        Update();
        return null;
    }

    private void ApplyPreferences()
    {
        var loaded = MacPreferenceStore.Load(_preferencesPath, _bootId);
        _preferences = loaded.Preferences; _preferencesWritable = loaded.CanSave;
        UiText.Apply(_preferences.Language);
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
        IndividualProcesses.IsChecked = !Grouped;
        RestoreTables();
        JobsView.SortIndex = _preferences.JobSort; JobsView.SortAscending = _preferences.JobAscending ?? _preferences.JobSort < 3; JobsView.FilterIndex = _preferences.JobFilter; JobsView.RowLimit = _preferences.JobLimit;
        _source.PrimaryDiskId = _preferences.PrimaryDiskId; _source.SecondaryDiskId = _preferences.SecondaryDiskId;
        UpdateDiskLayout();
        RestoreCharts();
        DataTabs.SelectedIndex = _preferences.DataTab; SetHistoryRange(_preferences.HistoryRange);
        AlwaysOnTop.IsChecked = _preferences.AlwaysOnTop;
        _keepHistory = _preferences.KeepHistory;
        _session.History.Resize(RangeSeconds);
        _source.NetworkInterfaceId = _preferences.NetworkId;
        _captureOptions = _preferences.Captures;
        CaptureToolbar.Update(AutomaticCaptures, _captureOptions.Enabled, _captures.Count);
        _captureRules = new(_captureOptions.Rules, TimeSpan.FromSeconds(_captureOptions.CooldownSeconds), Environment.ProcessorCount);
        UpdateRetention();
        _viewState = new(_preferences with { Width = Width, Height = Height, CompactMode = _preferences.StartupView switch { "Full" => false, "Compact" => true, _ => _preferences.CompactMode } });
        UpdateTabSummary();
        ApplyViewLayout();
    }

    private MacPreferences PreferencesSnapshot()
    {
        // Native resizes can leave requested Width/Height stale; save measured client bounds.
        _viewState.ObserveSize(ClientSize, IsVisible && WindowState == WindowState.Normal);
        return _viewState.ApplyTo(_preferences with
        {
            ProcessAscending = _processAscending, UsageAscending = _usageAscending,
            ProcessLimit = SelectedLimit(ProcessLimitBox), UsageLimit = SelectedLimit(UsageLimitBox),
            ProcessAppsOnly = ProcessAppsOnly.IsChecked == true, UsageAppsOnly = UsageAppsOnly.IsChecked == true,
            ActionsHidden = _actionBars.Hidden, ProcessActionsHidden = _actionBars.Hidden,
            ChartMode = Chart.Mode, VisibleMetrics = Chart.VisibleMetrics, FocusMetric = Chart.FocusMetric,
            BootId = _bootId, Grouped = Grouped, PinnedOnly = PinnedOnly.IsChecked == true,
            Pins = _pins.Snapshot(), ProcessSort = ProcessSortBox.SelectedIndex, UsageSort = UsageSortBox.SelectedIndex,
            DataTab = DataTabs.SelectedIndex, JobSort = JobsView.SortIndex, JobAscending = JobsView.SortAscending, JobFilter = JobsView.FilterIndex, JobLimit = JobsView.RowLimit, HistoryRange = _historyRange, NetworkId = _source.NetworkInterfaceId,
            AlwaysOnTop = Topmost, Captures = _captureOptions, KeepHistory = _keepHistory,
            DashboardRatio = MainLayout.RowDefinitions[1].Height.Value / Math.Max(0.01, MainLayout.RowDefinitions[3].Height.Value)
        });
    }

    private void ShowFromShortcut() => Dispatcher.UIThread.Post(() =>
    {
        if (_closing) return;
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); MacDesktopIntegration.Activate();
    });

    private async Task StartAsync()
    {
        var shortcutError = MacDesktopIntegration.SetShortcut(_preferences.ShowShortcut);
        if (shortcutError is not null) _operationStatus = UiText.Get(shortcutError);
        var keep = _keepHistory;
        var range = RangeSeconds;
        var restored = await Task.Run(() => keep
            ? MacHistoryStore.Load(_historyPath, range, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()) : new HistoryLoadResult([]));
        var reports = await Task.Run(() => MacCaptureReports.LoadRecords(_captureOptions.ReportFolder));
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
        MacDesktopIntegration.Activated -= ShowFromShortcut; MacDesktopIntegration.Dispose();
        HideResourceDetails();
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
