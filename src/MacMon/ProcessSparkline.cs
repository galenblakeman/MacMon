using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MacMon;

public sealed class ProcessSparkline : Control
{
    public static readonly StyledProperty<ProcessTrail?> TrailProperty =
        AvaloniaProperty.Register<ProcessSparkline, ProcessTrail?>(nameof(Trail));
    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<ProcessSparkline, IBrush>(nameof(Stroke), Brushes.MediumAquamarine);
    public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    static ProcessSparkline() => AffectsRender<ProcessSparkline>(TrailProperty, StrokeProperty);
    public ProcessTrail? Trail { get => GetValue(TrailProperty); set => SetValue(TrailProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Trail?.Values is not { Count: > 1 } values) return;
        var maximum = Math.Max(1, values.Max() ?? 1);
        var pen = new Pen(Stroke, 1.5);
        Point? previous = null;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } value) { previous = null; continue; }
            var point = new Point(i * Bounds.Width / (values.Count - 1),
                Bounds.Height * (1 - Math.Clamp(value / maximum, 0, 1)));
            if (previous is { } start) context.DrawLine(pen, start, point);
            previous = point;
        }
    }
}
