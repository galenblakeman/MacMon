using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using WinMon.Monitoring;

namespace MacMon;

public sealed class CpuChart : Control
{
    public static readonly StyledProperty<IBrush> PlotBrushProperty = AvaloniaProperty.Register<CpuChart, IBrush>(nameof(PlotBrush), Brushes.Transparent);
    public IBrush PlotBrush { get => GetValue(PlotBrushProperty); set => SetValue(PlotBrushProperty, value); }
    public static readonly StyledProperty<IBrush> CpuBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(CpuBrush), new SolidColorBrush(Color.Parse("#6DE3CB")));
    public IBrush CpuBrush { get => GetValue(CpuBrushProperty); set => SetValue(CpuBrushProperty, value); }
    public static readonly StyledProperty<IBrush> MemoryBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(MemoryBrush), new SolidColorBrush(Color.Parse("#BD9AFF")));
    public IBrush MemoryBrush { get => GetValue(MemoryBrushProperty); set => SetValue(MemoryBrushProperty, value); }
    public static readonly StyledProperty<IBrush> NetworkBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(NetworkBrush), new SolidColorBrush(Color.Parse("#73BFFF")));
    public IBrush NetworkBrush { get => GetValue(NetworkBrushProperty); set => SetValue(NetworkBrushProperty, value); }
    public static readonly StyledProperty<IBrush> StorageBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(StorageBrush), new SolidColorBrush(Color.Parse("#F6C85F")));
    public IBrush StorageBrush { get => GetValue(StorageBrushProperty); set => SetValue(StorageBrushProperty, value); }
    public static readonly StyledProperty<IBrush> SecondDiskBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(SecondDiskBrush), Brushes.Violet);
    public IBrush SecondDiskBrush { get => GetValue(SecondDiskBrushProperty); set => SetValue(SecondDiskBrushProperty, value); }
    public static readonly StyledProperty<IBrush> GpuBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(GpuBrush), Brushes.Orange);
    public IBrush GpuBrush { get => GetValue(GpuBrushProperty); set => SetValue(GpuBrushProperty, value); }
    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(GridBrush), new SolidColorBrush(Color.Parse("#394959")));
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public static readonly StyledProperty<IBrush> MarkerBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(MarkerBrush), new SolidColorBrush(Color.Parse("#FFFFFF")));
    public IBrush MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    static CpuChart() => AffectsRender<CpuChart>(CpuBrushProperty, MemoryBrushProperty, NetworkBrushProperty, StorageBrushProperty, PlotBrushProperty, GridBrushProperty, MarkerBrushProperty, GpuBrushProperty, SecondDiskBrushProperty);
    private IReadOnlyList<MonitorSample> _samples = [];
    private IReadOnlyList<MonitorSample> _visible = [];
    private double[] _maxima = [100, 100, 1024, 1024, 100, 1024];
    public IReadOnlyList<MacCaptureRecord> Captures { get; set; } = [];
    public event Action<MacCaptureRecord>? CaptureSelected;
    public int VisibleMetrics { get; set; } = 31;
    public ResourceChartMode Mode { get; set; }
    public int FocusMetric { get; set; }
    public int RangeSeconds { get; set; } = 120;
    public long? SelectedTimestamp { get; set; }
    public event Action<MonitorSample>? SampleSelected;
    public event Action<MonitorSample?>? SampleHovered;
    public event Action? LiveRequested;
    public MonitorSample? HoveredSample { get; private set; }
    private Point? _hoverPoint;
    public CpuChart()
    {
        Focusable = true;
        AutomationProperties.SetName(this, "Resource history");
        AutomationProperties.SetHelpText(this, "Left and Right inspect samples; Home and End select boundaries; Escape returns to Live. Hover previews readings; click captures a moment.");
        ToolTip.SetShowDelay(this, 100);
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hoverPoint = e.GetPosition(this);
        RefreshHover();
    }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHover();
    }
    public void ClearHover() { _hoverPoint = null; RefreshHover(); }
    private void RefreshHover()
    {
        var plot = _hoverPoint is { } point ? Panels.Select(ResourceCharts.Plot).FirstOrDefault(p => p.Contains(point)) : default;
        var sample = _hoverPoint is { } position && plot.Width > 0
            ? HistoryInspection.Nearest(_samples, RangeSeconds, (position.X - plot.X) / plot.Width) : null;
        HoveredSample = sample;
        var captured = _visible.FirstOrDefault(s => s.MonotonicTimestamp == SelectedTimestamp);
        var tipSample = captured ?? sample;
        ToolTip.SetTip(this, tipSample is null ? null : (captured is null ? "" : "Captured · ") + ResourceCharts.Describe(tipSample) + "\nHistory scales: " +
            string.Join(" · ", ResourceCharts.Metrics(VisibleMetrics).Select(i => $"{ResourceCharts.Names[i]} {ResourceCharts.Scale(i, Maximum(i))}")));
        if (captured is not null && IsFocused) ToolTip.SetIsOpen(this, true);
        else if (tipSample is null) ToolTip.SetIsOpen(this, false);
        SampleHovered?.Invoke(sample);
        InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Escape) { ClearHover(); LiveRequested?.Invoke(); ToolTip.SetIsOpen(this, false); e.Handled = true; return; }
        var offset = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Home => int.MinValue, Key.End => int.MaxValue, _ => 0 };
        if (offset == 0 || Panels.Length == 0) return;
        ClearHover();
        if (HistoryInspection.Navigate(_samples, RangeSeconds, SelectedTimestamp, offset) is { } sample)
        {
            SampleSelected?.Invoke(sample);
            RefreshHover();
            AutomationProperties.SetName(this, $"Resource history. Captured {ResourceCharts.Describe(sample)}");
        }
        e.Handled = true;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);
    protected override void OnLostFocus(FocusChangedEventArgs e)
    { base.OnLostFocus(e); ToolTip.SetIsOpen(this, false); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { ToolTip.SetIsOpen(this, false); base.OnDetachedFromVisualTree(e); }
    public double Maximum(int metric) => _maxima[metric];
    private IBrush Brush(int metric) => metric switch { 0 => CpuBrush, 1 => MemoryBrush, 2 => NetworkBrush, 4 => GpuBrush, 5 => SecondDiskBrush, _ => StorageBrush };
    private ChartPanel[] Panels => ResourceCharts.Panels(new Size(Bounds.Width, Math.Max(0, Bounds.Height - 22)), VisibleMetrics, Mode, FocusMetric);
    private double X(long time, Rect plot) => plot.X + plot.Width * (1 -
        (_samples[^1].MonotonicTimestamp - time) / (double)Stopwatch.Frequency / RangeSeconds);
    private static int CaptureMetricIndex(MacCaptureRecord capture) => capture.Metric switch
    { CaptureMetric.Memory => 1, CaptureMetric.Network => 2, CaptureMetric.Gpu => 4, _ => 0 };
    private bool MarkerVisible(MacCaptureRecord capture, ChartPanel panel) => _samples.Count > 0 &&
        capture.Peak.MonotonicTimestamp > 0 && panel.Metrics.Contains(CaptureMetricIndex(capture)) &&
        (_samples[^1].MonotonicTimestamp - capture.Peak.MonotonicTimestamp) / (double)Stopwatch.Frequency is var age && age >= 0 && age <= RangeSeconds;

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _samples.Count == 0) return;
        Focus();
        var point = e.GetPosition(this);
        foreach (var panel in Panels.Where(panel => ResourceCharts.Plot(panel).Contains(point)))
        {
            var plot = ResourceCharts.Plot(panel);
            var marker = Captures.FirstOrDefault(c => MarkerVisible(c, panel) && Math.Abs(X(c.Peak.MonotonicTimestamp, plot) - point.X) < 7 && point.Y - plot.Y < 14);
            if (marker is not null) { CaptureSelected?.Invoke(marker); e.Handled = true; return; }
            if (HistoryInspection.Nearest(_samples, RangeSeconds, (point.X - plot.X) / plot.Width) is { } sample)
            {
                SampleSelected?.Invoke(sample); RefreshHover(); e.Handled = true;
                // The tooltip service dismisses tips during pointer press routing.
                // Reopen the captured reading after that input has completed.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (SelectedTimestamp is not null && IsFocused) RefreshHover();
                });
            }
        }
    }

    public void Update(IReadOnlyList<MonitorSample> samples)
    {
        _samples = samples;
        _visible = HistoryInspection.VisibleSamples(samples, RangeSeconds);
        _maxima = Enumerable.Range(0, ResourceCharts.Names.Length).Select(i => ResourceCharts.Maximum(_visible, RangeSeconds, i)).ToArray();
        var captured = _visible.FirstOrDefault(s => s.MonotonicTimestamp == SelectedTimestamp);
        AutomationProperties.SetName(this, SelectedTimestamp is null ? UiText.Get("Resource history. Live") : captured is null
            ? "Resource history. Captured sample is outside the visible range" : $"Resource history. Captured {ResourceCharts.Describe(captured)}");
        RefreshHover();
    }
    private void Label(DrawingContext context, string text, Point point, IBrush brush) => context.DrawText(
        new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter, Arial"), 11, brush), point);

    public static string[] TimeLabels(DateTimeOffset end, int seconds) => Enumerable.Range(0, 5)
        .Select(i => end.AddSeconds(-seconds * (1 - i / 4d)).ToString(seconds <= 600 ? "HH:mm:ss" : "HH:mm")).ToArray();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.DrawRectangle(PlotBrush, null, new Rect(Bounds.Size));
        if (Panels.Length == 0) { Label(context, "Select a metric to display history", new(8, 8), MarkerBrush); return; }
        foreach (var panel in Panels)
        {
            var plot = ResourceCharts.Plot(panel);
            if (plot.Height <= 0 || plot.Width <= 0) continue;
            var metric = panel.Metrics[0];
            if (panel.SideLabel)
            {
                Label(context, UiText.Get(ResourceCharts.Names[metric]), new(panel.Bounds.X + 2, panel.Bounds.Y + Math.Max(0, panel.Bounds.Height / 2 - 13)), Brush(metric));
                if (panel.Bounds.Height >= 32) Label(context, ResourceCharts.Scale(metric, Maximum(metric)),
                    new(panel.Bounds.X + 2, panel.Bounds.Y + panel.Bounds.Height / 2 + 1), MarkerBrush);
            }
            else if (panel.Metrics.Length == 1) Label(context, UiText.Get(ResourceCharts.Names[metric]), panel.Bounds.TopLeft + new Vector(5, 4), Brush(metric));
            for (var i = 0; i <= 4; i++) context.DrawLine(new Pen(GridBrush, 1),
                new(plot.X, plot.Y + plot.Height * i / 4), new(plot.Right, plot.Y + plot.Height * i / 4));
            for (var i = 0; i <= 4; i++) context.DrawLine(new Pen(GridBrush, 1),
                new(plot.X + plot.Width * i / 4, plot.Y), new(plot.X + plot.Width * i / 4, plot.Bottom));
            if (_samples.Count == 0) continue;
            using (context.PushClip(plot))
            {
                foreach (var series in panel.Metrics)
                    if (Mode == ResourceChartMode.Heatmap) DrawHeatmap(context, plot, series);
                    else DrawSeries(context, plot, series);
                foreach (var capture in Captures.Where(c => MarkerVisible(c, panel)))
                {
                    var x = X(capture.Peak.MonotonicTimestamp, plot);
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                    { path.BeginFigure(new(x, plot.Y + 9), true); path.LineTo(new(x - 5, plot.Y)); path.LineTo(new(x + 5, plot.Y)); path.EndFigure(true); }
                    context.DrawGeometry(MarkerBrush, null, geometry);
                }
                if ((SelectedTimestamp ?? HoveredSample?.MonotonicTimestamp) is { } selected)
                {
                    var x = X(selected, plot);
                    if (x >= plot.X && x <= plot.Right) context.DrawLine(new Pen(MarkerBrush, 1), new(x, plot.Y), new(x, plot.Bottom));
                }
            }
        }
        if (_samples.Count > 0)
        {
            var plot = ResourceCharts.Plot(Panels.Last());
            var labels = TimeLabels(_samples[^1].Timestamp, RangeSeconds);
            for (var i = 0; i < labels.Length; i++)
            {
                var text = new FormattedText(labels[i], System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter, Arial"), 11, MarkerBrush);
                var x = plot.X + plot.Width * i / 4 - text.Width / 2;
                context.DrawText(text, new(Math.Clamp(x, 0, Math.Max(0, Bounds.Width - text.Width)), Bounds.Height - 17));
            }
        }
    }

    private void DrawSeries(DrawingContext context, Rect plot, int metric)
    {
        var line = new Pen(Brush(metric), 2);
        Point? previous = null;
        MonitorSample? prior = null;
        var maximum = Maximum(metric);
        foreach (var sample in _visible)
        {
            if (ResourceCharts.Value(sample, metric) is not { } value) { previous = null; continue; }
            var point = new Point(X(sample.MonotonicTimestamp, plot), plot.Bottom - plot.Height * Math.Clamp(value / maximum, 0, 1));
            if (previous is { } start && prior is not null &&
                (sample.MonotonicTimestamp - prior.MonotonicTimestamp) / (double)Stopwatch.Frequency <= 2.5 &&
                (metric != 2 || sample.Resources.NetworkId == prior.Resources.NetworkId) &&
                (metric != 4 || sample.Resources.GpuId == prior.Resources.GpuId) &&
                (metric != 3 || sample.Resources.DiskName == prior.Resources.DiskName) &&
                (metric != 5 || sample.Resources.SecondaryDiskName == prior.Resources.SecondaryDiskName)) context.DrawLine(line, start, point);
            // A single observation remains visible without drawing across missing time.
            context.DrawEllipse(Brush(metric), null, point, 1, 1);
            previous = point; prior = sample;
        }
    }
    private void DrawHeatmap(DrawingContext context, Rect plot, int metric)
    {
        var maximum = Maximum(metric);
        var samples = _visible;
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (ResourceCharts.Value(sample, metric) is not { } value) continue;
            var x = X(sample.MonotonicTimestamp, plot);
            // Do not paint across a missing interval, source change or wake gap.
            var end = i + 1 < samples.Count ? Math.Min(samples[i + 1].MonotonicTimestamp, sample.MonotonicTimestamp + Stopwatch.Frequency) : sample.MonotonicTimestamp + Stopwatch.Frequency;
            using (context.PushOpacity(.08 + .92 * Math.Clamp(value / maximum, 0, 1)))
                context.DrawRectangle(Brush(metric), null, new Rect(x, plot.Y, Math.Max(1, X(end, plot) - x), plot.Height));
        }
    }
}
