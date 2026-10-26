using Avalonia.Controls;
using Avalonia.Automation;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class AutoCaptureWindow : Window
{
    public AutoCaptureWindow() : this(MacCaptureOptions.Default) { }
    public AutoCaptureWindow(MacCaptureOptions options)
    {
        InitializeComponent();
        options = options.Normalized();
        EnabledCapture.IsChecked = options.Enabled;
        Cooldown.Value = options.CooldownSeconds;
        var controls = new[]
        {
            (CaptureMetric.Cpu, CpuEnabled, CpuThreshold, CpuSeconds),
            (CaptureMetric.Memory, MemoryEnabled, MemoryThreshold, MemorySeconds),
            (CaptureMetric.Network, NetworkEnabled, NetworkThreshold, NetworkSeconds),
            (CaptureMetric.Process, ProcessEnabled, ProcessThreshold, ProcessSeconds)
        };
        foreach (var (metric, enabled, threshold, seconds) in controls)
        {
            var rule = options.Rules.First(rule => rule.Metric == metric);
            enabled.IsChecked = rule.Enabled;
            threshold.Value = (decimal)rule.Threshold;
            seconds.Value = rule.Seconds;
            enabled.SetValue(AutomationProperties.NameProperty, enabled.Content?.ToString());
            threshold.SetValue(AutomationProperties.NameProperty, $"{enabled.Content} threshold");
            seconds.SetValue(AutomationProperties.NameProperty, $"{enabled.Content} duration seconds");
        }
        SaveChanges.Click += (_, _) => Close(new MacCaptureOptions(EnabledCapture.IsChecked == true,
            controls.Select(control => new CaptureRuleSetting(control.Item1, control.Item2.IsChecked == true,
                (double)(control.Item3.Value ?? 1), (int)(control.Item4.Value ?? 1))).ToArray(),
            (int)(Cooldown.Value ?? 60)).Normalized());
        CancelChanges.Click += (_, _) => Close(null);
    }
}
