using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WinMon.Monitoring;

namespace MacMon;

public sealed class CpuChart : Control
{
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
    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(GridBrush), new SolidColorBrush(Color.Parse("#394959")));
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public static readonly StyledProperty<IBrush> MarkerBrushProperty =
        AvaloniaProperty.Register<CpuChart, IBrush>(nameof(MarkerBrush), new SolidColorBrush(Color.Parse("#FFFFFF")));
    public IBrush MarkerBrush { get => GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    static CpuChart() => AffectsRender<CpuChart>(CpuBrushProperty, MemoryBrushProperty, NetworkBrushProperty, StorageBrushProperty, GridBrushProperty, MarkerBrushProperty);
    private IReadOnlyList<MonitorSample> _samples = [];
    private IReadOnlyList<MonitorSample> _visible = [];
    private double[] _maxima = [100, 100, 1024, 1024];
    public IReadOnlyList<MacCaptureRecord> Captures { get; set; } = [];
    public event Action<MacCaptureRecord>? CaptureSelected;
    public int VisibleMetrics { get; set; } = 15;
    public ResourceChartMode Mode { get; set; }
    public int FocusMetric { get; set; }
    public int RangeSeconds { get; set; } = 120;
    public long? SelectedTimestamp { get; set; }
    public event Action<MonitorSample>? SampleSelected;
    public double Maximum(int metric) => _maxima[metric];
    private IBrush Brush(int metric) => metric switch { 0 => CpuBrush, 1 => MemoryBrush, 2 => NetworkBrush, _ => StorageBrush };
    private ChartPanel[] Panels => ResourceCharts.Panels(Bounds.Size, VisibleMetrics, Mode, FocusMetric);
    private double X(long time, Rect plot) => plot.X + plot.Width * (1 -
        (_samples[^1].MonotonicTimestamp - time) / (double)Stopwatch.Frequency / RangeSeconds);
    private static int CaptureMetricIndex(MacCaptureRecord capture) => capture.Metric switch
    { CaptureMetric.Memory => 1, CaptureMetric.Network => 2, _ => 0 };
    private bool MarkerVisible(MacCaptureRecord capture, ChartPanel panel) => _samples.Count > 0 &&
        capture.Peak.MonotonicTimestamp > 0 && panel.Metrics.Contains(CaptureMetricIndex(capture)) &&
        (_samples[^1].MonotonicTimestamp - capture.Peak.MonotonicTimestamp) / (double)Stopwatch.Frequency is var age && age >= 0 && age <= RangeSeconds;

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _samples.Count == 0) return;
        var point = e.GetPosition(this);
        foreach (var panel in Panels.Where(panel => ResourceCharts.Plot(panel).Contains(point)))
        {
            var plot = ResourceCharts.Plot(panel);
            var marker = Captures.FirstOrDefault(c => MarkerVisible(c, panel) && Math.Abs(X(c.Peak.MonotonicTimestamp, plot) - point.X) < 7 && point.Y - plot.Y < 14);
            if (marker is not null) { CaptureSelected?.Invoke(marker); return; }
            if (HistoryInspection.Nearest(_samples, RangeSeconds, (point.X - plot.X) / plot.Width) is { } sample)
                SampleSelected?.Invoke(sample);
        }
    }

    public void Update(IReadOnlyList<MonitorSample> samples)
    {
        _samples = samples;
        _visible = HistoryInspection.VisibleSamples(samples, RangeSeconds);
        _maxima = Enumerable.Range(0, 4).Select(i => ResourceCharts.Maximum(_visible, RangeSeconds, i)).ToArray();
        InvalidateVisual();
    }
    private void Label(DrawingContext context, string text, Point point, IBrush brush) => context.DrawText(
        new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter, Arial"), 11, brush), point);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (Panels.Length == 0) { Label(context, "Select a metric to display history", new(8, 8), MarkerBrush); return; }
        foreach (var panel in Panels)
        {
            var plot = ResourceCharts.Plot(panel);
            if (plot.Height <= 0 || plot.Width <= 0) continue;
            var metric = panel.Metrics[0];
            var title = panel.Metrics.Length > 1 ? "Each series uses its labeled scale" :
                $"{ResourceCharts.Names[metric]} · {ResourceCharts.Scale(metric, Maximum(metric))}";
            Label(context, title, panel.Bounds.TopLeft, panel.Metrics.Length > 1 ? MarkerBrush : Brush(metric));
            for (var i = 0; i <= 4; i++) context.DrawLine(new Pen(GridBrush, 1),
                new(plot.X, plot.Y + plot.Height * i / 4), new(plot.Right, plot.Y + plot.Height * i / 4));
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
                if (SelectedTimestamp is { } selected)
                {
                    var x = X(selected, plot);
                    if (x >= plot.X && x <= plot.Right) context.DrawLine(new Pen(MarkerBrush, 1), new(x, plot.Y), new(x, plot.Bottom));
                }
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
                (metric != 2 || sample.Resources.NetworkId == prior.Resources.NetworkId)) context.DrawLine(line, start, point);
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
