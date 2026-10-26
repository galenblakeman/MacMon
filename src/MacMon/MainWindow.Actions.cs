using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class MainWindow
{
    private void UpdateTitleBarInsets()
    {
        // Preserve the native macOS traffic-light controls while sharing their title row.
        var inset = OperatingSystem.IsMacOS() && IsExtendedIntoWindowDecorations ? 76 : 0;
        TitleActions.Margin = new Avalonia.Thickness(inset, 0, 0, 0);
        CompactPanel.SetTitleInset(inset);
    }
    private void UpdateTabSummary()
    {
        ProcessCountLabel.IsVisible = DataTabs.SelectedIndex == 0;
        JobSummary.IsVisible = DataTabs.SelectedIndex == 1;
        UsageStatus.IsVisible = DataTabs.SelectedIndex == 2;
    }
    private async Task ShowHardwareInfo()
    {
        var r = _session.HasLiveSample ? _session.Latest?.Resources : null;
        var text = $"MacMon {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\n\n" +
            $"Computer: {Environment.MachineName}\nmacOS: {Environment.OSVersion.Version}\nArchitecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}\n" +
            $"Logical CPU cores: {Environment.ProcessorCount}\nInstalled memory: {GiB(r?.TotalMemoryBytes)}\n" +
            $"Data volume: {r?.DiskName ?? "—"}\nAvailable: {GiB(r?.DiskFreeBytes)} / {GiB(r?.DiskTotalBytes)}\n\n" +
            "Network interfaces:\n" + string.Join("\n", _source.NetworkInterfaces.Select(n => $"{n.Name} · {n.Id}"));
        var dialog = new ProcessDialog("About MacMon and hardware", text);
        if (CompactMode) dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        await dialog.ShowDialog<bool>(this);
    }
    private void OnRowPinClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: ProcessRow row } || row.Grouped != Grouped ||
            !_historyOrigins.CanPin(TrailHistory().LastOrDefault(), Grouped, _inspection.IsHistorical)) return;
        // Use the clicked row's exact lifetime, never a re-resolved PID after the click.
        _pins.Toggle(row.Process, Grouped); UpdateRetention(); UpdateProcesses(); args.Handled = true;
    }
    private bool _refreshingUsage;
    private string? _usageSelection;
    private void InitializeUsageActions()
    {
        UsageList.SelectionChanged += (_, _) =>
        {
            if (!_refreshingUsage) _usageSelection = (UsageList.SelectedItem as UsageRow)?.App.Key;
            UpdateUsageSelection();
        };
        UsageProperties.Click += async (_, _) =>
        {
            if (UsageList.SelectedItem is UsageRow row) await new ProcessDialog("App usage", UsageDetails(row)).ShowDialog<bool>(this);
        };
        CopyUsageApp.Click += async (_, _) =>
        {
            if (UsageList.SelectedItem is UsageRow row && Clipboard is { } clipboard) await clipboard.SetTextAsync(UsageDetails(row));
        };
        RevealUsageApp.Click += (_, _) =>
        {
            if (UsageList.SelectedItem is not UsageRow row || !File.Exists(row.Path)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/open")
                { UseShellExecute = false, ArgumentList = { "-R", row.Path } }); }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
            { _operationStatus = $"Could not reveal executable: {ex.Message}"; }
        };
    }
    private static string UsageDetails(UsageRow row) => $"{row.Name}\nCPU time: {row.Cpu}\nBusy share: {row.Share}\nDisk I/O: {row.DiskIo} MiB\n" +
        $"Seen (running): {row.Processes}\nLast active: {row.LastActive}\nExecutable: {row.Path}";
    private void UpdateUsageSelection()
    {
        var row = UsageList.SelectedItem as UsageRow;
        UsageProperties.IsEnabled = CopyUsageApp.IsEnabled = row is not null;
        RevealUsageApp.IsEnabled = row is not null && File.Exists(row.Path);
        UsageSelection.Text = row is null ? "Select an app" : row.Path;
        ToolTip.SetTip(UsageSelection, row?.Path);
    }
    private async Task CopyUsageRows()
    {
        if (Clipboard is not { } clipboard) return;
        var rows = (UsageList.ItemsSource as IEnumerable<UsageRow> ?? []).ToArray();
        var text = UsagePresentation.Report(rows, _session.Usage.Since);
        await clipboard.SetTextAsync(text); _operationStatus = $"Copied {rows.Length} usage rows"; Update();
    }
}
