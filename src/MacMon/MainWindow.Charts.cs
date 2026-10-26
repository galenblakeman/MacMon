using Avalonia.Controls;

namespace MacMon;

public sealed partial class MainWindow
{
    private bool _restoringCharts;
    private int _historyRange;
    private CheckBox CpuVisible => CpuGauge.SeriesToggle;
    private CheckBox MemoryVisible => MemoryGauge.SeriesToggle;
    private CheckBox NetworkVisible => NetworkGauge.SeriesToggle;
    private CheckBox StorageVisible => StorageGauge.SeriesToggle;
    private CheckBox[] MetricSwitches => [CpuVisible, MemoryVisible, NetworkVisible, StorageVisible, GpuGauge.SeriesToggle, SecondDiskGauge.SeriesToggle];
    private RadioButton[] RangeChips => [Range2, Range10, Range30, Range60];
    private sealed record ChartChoice(string Label, ResourceChartMode Mode, int Focus = 0)
    { public override string ToString() => UiText.Get(Label); }
    private static readonly ChartChoice[] ChartChoices = [new("Overlay", ResourceChartMode.Overlay),
        new("Small multiples", ResourceChartMode.SmallMultiples), new("Heatmap", ResourceChartMode.Heatmap),
        new("Focus · CPU", ResourceChartMode.Focus), new("Focus · Memory", ResourceChartMode.Focus, 1),
        new("Focus · Disk", ResourceChartMode.Focus, 3), new("Focus · Network", ResourceChartMode.Focus, 2), new("Focus · GPU", ResourceChartMode.Focus, 4), new("Focus · Disk 2", ResourceChartMode.Focus, 5)];
    private ChartChoice[] AvailableChartChoices => ChartChoices.Where(c => c.Focus != 5 || SecondDiskCard.IsVisible).ToArray();
    private void UpdateDiskLayout()
    {
        var enabled = _source.SecondaryDiskId is not null;
        var selected = ChartModeBox.SelectedItem;
        SecondDiskCard.IsVisible = enabled; ResourceCards.Columns = enabled ? 6 : 5;
        CompactPanel.SetSecondDiskVisible(enabled);
        _restoringCharts = true;
        try
        {
            ChartModeBox.ItemsSource = AvailableChartChoices;
            ChartModeBox.SelectedItem = AvailableChartChoices.Contains(selected) ? selected : AvailableChartChoices[0];
        }
        finally { _restoringCharts = false; }
        ApplyChartChoices(false);
    }
    private void InitializeCharts()
    {
        Chart.SampleHovered += _ => UpdateChartReadout();
        Chart.LiveRequested += ReturnToLive;
        ChartModeBox.ItemsSource = AvailableChartChoices;
        ChartModeBox.SelectionChanged += (_, _) => ApplyChartChoices(true);
        foreach (var toggle in MetricSwitches) toggle.IsCheckedChanged += (_, _) => ApplyChartChoices(false);
        foreach (var (chip, index) in RangeChips.Select((chip, index) => (chip, index)))
            chip.IsCheckedChanged += (_, _) => { if (!_restoringCharts && chip.IsChecked == true) SetHistoryRange(index); };
    }
    private void SetHistoryRange(int index)
    {
        _historyRange = Math.Clamp(index, 0, 3);
        _restoringCharts = true;
        try { RangeChips[_historyRange].IsChecked = true; }
        finally { _restoringCharts = false; }
        _session.History.Resize(RangeSeconds); Chart.RangeSeconds = RangeSeconds;
        Update();
    }
    private void RestoreCharts()
    {
        _restoringCharts = true;
        try
        {
            ChartModeBox.SelectedIndex = Array.FindIndex(AvailableChartChoices, c => c.Mode == _preferences.ChartMode &&
                (c.Mode != ResourceChartMode.Focus || c.Focus == _preferences.FocusMetric));
            for (var i = 0; i < MetricSwitches.Length; i++) MetricSwitches[i].IsChecked = (_preferences.VisibleMetrics & (1 << i)) != 0;
        }
        finally { _restoringCharts = false; }
        ApplyChartChoices(false);
    }
    private void ApplyChartChoices(bool choseLayout)
    {
        if (_restoringCharts || ChartModeBox.SelectedItem is not ChartChoice choice) return;
        if (choseLayout && choice.Mode == ResourceChartMode.Focus)
        {
            _restoringCharts = true;
            try { MetricSwitches[choice.Focus].IsChecked = true; }
            finally { _restoringCharts = false; }
        }
        Chart.Mode = choice.Mode;
        Chart.VisibleMetrics = MetricSwitches.Select((toggle, i) => toggle.IsChecked == true ? 1 << i : 0).Sum() & (SecondDiskCard.IsVisible ? 63 : 31);
        Chart.FocusMetric = ResourceCharts.Focus(Chart.VisibleMetrics, choice.Focus);
        _restoringCharts = true;
        try
        {
            if (choice.Mode == ResourceChartMode.Focus && Chart.VisibleMetrics != 0)
                ChartModeBox.SelectedIndex = Array.FindIndex(AvailableChartChoices, c => c.Mode == ResourceChartMode.Focus && c.Focus == Chart.FocusMetric);
        }
        finally { _restoringCharts = false; }
        Chart.ClearHover(); Chart.InvalidateVisual(); UpdateChartLabels();
    }
    private void UpdateChartReadout()
    {
        LiveMode.Content = UiText.Get("● Live"); CaptureMode.Content = UiText.Get("◎ Capture");
        LiveMode.Classes.Set("active", !_inspection.IsHistorical);
        CaptureMode.Classes.Set("active", _inspection.IsHistorical);
        CaptureMode.IsEnabled = _session.Latest is not null;
        ToolTip.SetTip(LiveMode, "Return to current readings; sampling continues during inspection");
        ToolTip.SetTip(CaptureMode, "Inspect the latest retained sample; click the plot or use arrow keys to select another");
    }
    private void ReturnToLive()
    {
        if (!_historyOrigins.SameBoot(TrailHistory().LastOrDefault(), _session.LatestFull)) ClearSelection();
        _inspection.Live(); Chart.ClearHover(); Update();
    }
    private void UpdateChartLabels() => UpdateChartReadout();
}
