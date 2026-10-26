using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WinMon.Monitoring;

namespace MacMon;

public sealed class CpuChart : Control
{
    private IReadOnlyList<MonitorSample> _samples = [];
    public IReadOnlyList<MacCaptureRecord> Captures { get; set; } = [];
    public event Action<MacCaptureRecord>? CaptureSelected;
    public bool ShowActivity { get; set; }
    public double ActivityMaximum => Math.Max(1024, _samples.Where(sample => _samples.Count > 0 &&
        (_samples[^1].MonotonicTimestamp - sample.MonotonicTimestamp) / (double)Stopwatch.Frequency <= RangeSeconds)
        .Select(sample => Math.Max(NetworkTotal(sample) ?? 0, StorageTotal(sample) ?? 0)).DefaultIfEmpty().Max());
    private static double? NetworkTotal(MonitorSample sample) => sample.Resources.ReceiveBytesPerSecond + sample.Resources.SendBytesPerSecond;
    private static double? StorageTotal(MonitorSample sample) => sample.Resources.SecondaryDiskReadBytesPerSecond + sample.Resources.SecondaryDiskWriteBytesPerSecond;
    public int RangeSeconds { get; set; } = 120;
    public long? SelectedTimestamp { get; set; }
    public event Action<MonitorSample>? SampleSelected;

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bounds.Width <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (_samples.Count > 0)
        {
            var x = e.GetPosition(this).X;
            var marker = Captures.Where(capture => MarkerVisible(capture))
                .FirstOrDefault(capture => Math.Abs(MarkerX(capture) - x) < 7 && e.GetPosition(this).Y < 14);
            if (marker is not null) { CaptureSelected?.Invoke(marker); return; }
        }
        if (HistoryInspection.Nearest(_samples, RangeSeconds, e.GetPosition(this).X / Bounds.Width) is { } sample)
            SampleSelected?.Invoke(sample);
    }

    public void Update(IReadOnlyList<MonitorSample> samples)
    {
        _samples = samples;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#2C3947")), 1);
        for (var i = 0; i <= 4; i++)
            context.DrawLine(gridPen, new Point(0, height * i / 4), new Point(width, height * i / 4));
        if (_samples.Count == 0) return;
        var now = _samples[^1].MonotonicTimestamp;
        if (ShowActivity)
        {
            DrawSeries(context, width, height, now, NetworkTotal, "#73BFFF", true, ActivityMaximum);
            DrawSeries(context, width, height, now, StorageTotal, "#F6C85F", true, ActivityMaximum);
        }
        else
        {
            DrawSeries(context, width, height, now, sample => sample.TotalCpuPercent, "#6DE3CB", true);
            DrawSeries(context, width, height, now, sample => sample.Resources.MemoryPercent, "#BD9AFF", false);
        }
        foreach (var capture in Captures.Where(MarkerVisible))
        {
            var x = MarkerX(capture);
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(x, 9), true);
                path.LineTo(new Point(x - 5, 0)); path.LineTo(new Point(x + 5, 0)); path.EndFigure(true);
            }
            context.DrawGeometry(Brushes.White, null, geometry);
        }
        if (SelectedTimestamp is { } selected)
        {
            var age = (now - selected) / (double)Stopwatch.Frequency;
            if (age is >= 0 && age <= RangeSeconds)
                context.DrawLine(new Pen(Brushes.White, 1), new Point(width * (1 - age / RangeSeconds), 0),
                    new Point(width * (1 - age / RangeSeconds), height));
        }
    }

    private bool MarkerVisible(MacCaptureRecord capture)
    {
        if (_samples.Count == 0 || capture.Peak.MonotonicTimestamp <= 0 || ShowActivity != (capture.Metric == CaptureMetric.Network)) return false;
        var age = (_samples[^1].MonotonicTimestamp - capture.Peak.MonotonicTimestamp) / (double)Stopwatch.Frequency;
        return age >= 0 && age <= RangeSeconds;
    }
    private double MarkerX(MacCaptureRecord capture) => Bounds.Width * (1 -
        (_samples[^1].MonotonicTimestamp - capture.Peak.MonotonicTimestamp) / (double)Stopwatch.Frequency / RangeSeconds);

    private void DrawSeries(DrawingContext context, double width, double height, long now,
        Func<MonitorSample, double?> value, string color, bool interval, double maximum = 100)
    {
        var line = new Pen(new SolidColorBrush(Color.Parse(color)), 2);
        Point? previous = null;
        long previousTime = 0;
        foreach (var sample in _samples)
        {
            var age = (now - sample.MonotonicTimestamp) / (double)Stopwatch.Frequency;
            if (age > RangeSeconds) continue;
            if ((interval && sample.IsGap) || value(sample) is not { } cpu)
            {
                previous = null;
                continue;
            }

            var point = new Point(width * (1 - age / RangeSeconds), height * (1 - Math.Clamp(cpu, 0, maximum) / maximum));
            if (previous is { } start &&
                (sample.MonotonicTimestamp - previousTime) / (double)Stopwatch.Frequency <= 2.5)
                context.DrawLine(line, start, point);
            previous = point;
            previousTime = sample.MonotonicTimestamp;
        }
    }
}
