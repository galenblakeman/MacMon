using Avalonia.Controls;

namespace MacMon;

public sealed partial class MainWindow
{
    private bool _restoringCharts;
    private CheckBox[] MetricSwitches => [CpuVisible, MemoryVisible, NetworkVisible, StorageVisible];
    private void InitializeCharts()
    {
        ChartModeBox.SelectionChanged += (_, _) => ApplyChartChoices();
        FocusMetricBox.SelectionChanged += (_, _) =>
        {
            if (!_restoringCharts && FocusMetricBox.SelectedIndex is >= 0 and < 4)
                MetricSwitches[FocusMetricBox.SelectedIndex].IsChecked = true;
            ApplyChartChoices();
        };
        foreach (var toggle in MetricSwitches) toggle.IsCheckedChanged += (_, _) => ApplyChartChoices();
    }
    private void RestoreCharts()
    {
        _restoringCharts = true;
        try
        {
            ChartModeBox.SelectedIndex = (int)_preferences.ChartMode;
            FocusMetricBox.SelectedIndex = _preferences.FocusMetric;
            for (var i = 0; i < 4; i++) MetricSwitches[i].IsChecked = (_preferences.VisibleMetrics & (1 << i)) != 0;
        }
        finally { _restoringCharts = false; }
        ApplyChartChoices();
    }
    private void ApplyChartChoices()
    {
        if (_restoringCharts) return;
        Chart.Mode = (ResourceChartMode)Math.Clamp(ChartModeBox.SelectedIndex, 0, 3);
        Chart.VisibleMetrics = MetricSwitches.Select((toggle, i) => toggle.IsChecked == true ? 1 << i : 0).Sum();
        Chart.FocusMetric = ResourceCharts.Focus(Chart.VisibleMetrics, FocusMetricBox.SelectedIndex);
        _restoringCharts = true;
        try { FocusMetricBox.SelectedIndex = Chart.FocusMetric; }
        finally { _restoringCharts = false; }
        FocusMetricBox.IsVisible = Chart.Mode == ResourceChartMode.Focus;
        Chart.InvalidateVisual();
        UpdateChartLabels();
    }
    private void UpdateChartLabels()
    {
        for (var i = 0; i < 4; i++)
        {
            var label = $"{ResourceCharts.Names[i]} · {ResourceCharts.Scale(i, Chart.Maximum(i))}";
            MetricSwitches[i].Content = label;
            Avalonia.Automation.AutomationProperties.SetName(MetricSwitches[i], $"Show {label}");
            ToolTip.SetTip(MetricSwitches[i], i < 2 ? "Share of machine capacity" : "Combined throughput; independent scale, not device utilization");
        }
    }
}
