using Avalonia.Controls;

namespace MacMon;

public sealed partial class CompactView : UserControl
{
    public event Action? InfoRequested;
    public event Action<WinMon.Monitoring.CsvExportKind>? ExportRequested;
    public event Action? FullViewRequested;
    public event Action? SettingsRequested;
    public event Action<bool>? TopmostChanged;
    public ResourceGauge[] Gauges { get; }
    public bool AlwaysOnTop { get => CompactOnTop.IsChecked == true; set => CompactOnTop.IsChecked = value; }
    public string Status { get => LiveStatus.Text ?? ""; set { LiveStatus.Text = value; ToolTip.SetTip(LiveStatus, value); } }
    public CompactView()
    {
        InitializeComponent();
        Gauges = [Cpu, Memory, Network, Storage];
        CompactInfo.Click += (_, _) => InfoRequested?.Invoke();
        CompactResourcesCsv.Click += (_, _) => { CompactExport.Flyout?.Hide(); ExportRequested?.Invoke(WinMon.Monitoring.CsvExportKind.ResourceHistory); };
        CompactProcessesCsv.Click += (_, _) => { CompactExport.Flyout?.Hide(); ExportRequested?.Invoke(WinMon.Monitoring.CsvExportKind.ProcessSamples); };
        CompactUsageCsv.Click += (_, _) => { CompactExport.Flyout?.Hide(); ExportRequested?.Invoke(WinMon.Monitoring.CsvExportKind.UsageTotals); };
        ExpandView.Click += (_, _) => FullViewRequested?.Invoke();
        CompactSettings.Click += (_, _) => SettingsRequested?.Invoke();
        CompactOnTop.IsCheckedChanged += (_, _) => TopmostChanged?.Invoke(AlwaysOnTop);
    }
    public void SetTitleInset(double inset) => CompactTitleActions.Margin = new Avalonia.Thickness(inset, 0, 0, 0);
    public void FocusFullView() => ExpandView.Focus();
}
