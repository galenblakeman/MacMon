using Avalonia.Controls;

namespace MacMon;

public sealed partial class CompactView : UserControl
{
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
        ExpandView.Click += (_, _) => FullViewRequested?.Invoke();
        CompactSettings.Click += (_, _) => SettingsRequested?.Invoke();
        CompactOnTop.IsCheckedChanged += (_, _) => TopmostChanged?.Invoke(AlwaysOnTop);
    }
    public void FocusFullView() => ExpandView.Focus();
}
