using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using Avalonia.Automation;
using WinMon.Monitoring;

namespace MacMon;

public sealed partial class CaptureSettingsEditor : UserControl
{
    public CaptureSettingsEditor()
    {
        InitializeComponent();
        EnabledCapture.IsCheckedChanged += (_, _) => EnabledCapture.Bind(CheckBox.ContentProperty, UiText.Bind(EnabledCapture.IsChecked == true ? "On" : "Off"));
        Load(MacCaptureOptions.Default);
        ChangeFolder.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var folders = await storage.OpenFolderPickerAsync(new() { Title = UiText.Get("Save captures to"), AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) CaptureFolder.Text = path;
        };
        DefaultFolder.Click += (_, _) => CaptureFolder.Text = MacCaptureOptions.Default.ReportFolder;
        OpenFolder.Click += (_, _) =>
        {
            try
            {
                var path = CaptureFolder.Text ?? MacCaptureOptions.Default.ReportFolder;
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, ArgumentList = { path } });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { FolderStatus.Text = ex.Message; }
        };
    }
    public void Load(MacCaptureOptions options)
    {
        options = options.Normalized();
        EnabledCapture.IsChecked = options.Enabled;
        CaptureFolder.Text = options.ReportFolder; NotifyCapture.IsChecked = options.Notify;
        NotifyCapture.IsEnabled = MacDesktopIntegration.LoginStatus >= 0;
        Cooldown.Value = options.CooldownSeconds;
        var controls = new[]
        {
            (CaptureMetric.Cpu, CpuEnabled, CpuThreshold, CpuSeconds),
            (CaptureMetric.Gpu, GpuEnabled, GpuThreshold, GpuSeconds),
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
            threshold.SetValue(AutomationProperties.NameProperty, UiText.Format("{0} threshold", enabled.Content));
            seconds.SetValue(AutomationProperties.NameProperty, UiText.Format("{0} duration seconds", enabled.Content));
        }
    }
    public MacCaptureOptions Value => new MacCaptureOptions(EnabledCapture.IsChecked == true,
        new[]
        {
            (CaptureMetric.Cpu, CpuEnabled, CpuThreshold, CpuSeconds),
            (CaptureMetric.Gpu, GpuEnabled, GpuThreshold, GpuSeconds),
            (CaptureMetric.Memory, MemoryEnabled, MemoryThreshold, MemorySeconds),
            (CaptureMetric.Network, NetworkEnabled, NetworkThreshold, NetworkSeconds),
            (CaptureMetric.Process, ProcessEnabled, ProcessThreshold, ProcessSeconds)
        }.Select(control => new CaptureRuleSetting(control.Item1, control.Item2.IsChecked == true,
            (double)(control.Item3.Value ?? 1), (int)(control.Item4.Value ?? 1))).ToArray(),
        (int)(Cooldown.Value ?? 60), CaptureFolder.Text == MacCaptureOptions.Default.ReportFolder ? null : CaptureFolder.Text, NotifyCapture.IsChecked == true).Normalized();
}
