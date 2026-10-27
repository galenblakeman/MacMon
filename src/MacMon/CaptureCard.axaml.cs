using Avalonia.Controls;
using WinMon.Monitoring;

namespace MacMon;

/// <summary>An interactive flyout; copy/report actions carry the displayed snapshot, not the current selection.</summary>
public sealed partial class CaptureCard : UserControl
{
    private readonly Flyout _flyout;
    private string? _text;
    private MacCaptureRecord? _report;
    public event Action<string>? CopyRequested;
    public event Action? LiveRequested;
    public event Action<MacCaptureRecord>? ReportRequested;
    public bool IsOpen => _flyout.IsOpen;
    public CaptureCard()
    {
        InitializeComponent();
        _flyout = new Flyout { Content = this, Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Transient };
        _flyout.FlyoutPresenterClasses.Add("captureCard");
        CopyReading.Click += (_, _) => { if (_text is { } text) CopyRequested?.Invoke(text); };
        ResumeLive.Click += (_, _) => { Hide(); LiveRequested?.Invoke(); };
        ViewReport.Click += (_, _) => { if (_report is { } report) ReportRequested?.Invoke(report); };
    }
    public void Update(MonitorSample? sample, string? text, MacCaptureRecord? report)
    {
        _text = sample is null ? null : text;
        _report = report is { State: "Saved" } && File.Exists(report.ReportPath) ? report : null;
        CaptureReading.Text = sample is null ? UiText.Get("Selected capture has expired.") : $"{sample.Timestamp:yyyy-MM-dd} · {ResourceCharts.Describe(sample)}";
        CopyReading.IsEnabled = _text is not null;
        ViewReport.IsVisible = _report is not null;
    }
    public void ShowAt(Control target)
    {
        if (target.IsEffectivelyVisible && TopLevel.GetTopLevel(target) is not null && !IsOpen) _flyout.ShowAt(target);
    }
    public void Hide() => _flyout.Hide();
    public void FocusActions() => (CopyReading.IsEnabled ? CopyReading : ResumeLive).Focus();
}
