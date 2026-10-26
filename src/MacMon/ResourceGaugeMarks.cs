using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MacMon;

/// <summary>The WinMon gauge geometry expressed in Avalonia drawing primitives.</summary>
public sealed class ResourceGaugeMarks : Control
{
    public string Kind { get; set; } = "Bowl";
    public IBrush Fill { get; set; } = Brushes.Teal;
    public IBrush Track { get; set; } = Brushes.Gray;
    public IBrush Marker { get; set; } = Brushes.White;
    public GaugeReading Reading { get; set; } = GaugeReading.Empty;
    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        if (Reading.Value is not { } value || Kind == "None" || Bounds.Width <= 2 || Bounds.Height <= 2) return;
        var level = Math.Clamp(value / Reading.Maximum, 0, 1);
        var peak = Math.Clamp((Reading.Peak ?? value) / Reading.Maximum, 0, 1);
        var size = Bounds.Size;
        var mark = new Pen(Marker, 2);
        if (Kind == "LeftBar")
        {
            dc.DrawRectangle(Track, new Pen(Track, 1), new Rect(size));
            var inner = new Rect(1, 2, size.Width - 2, size.Height - 4);
            dc.DrawRectangle(Fill, null, new Rect(inner.X, inner.Bottom - inner.Height * level, inner.Width, inner.Height * level));
            var y = inner.Bottom - inner.Height * peak;
            dc.DrawLine(mark, new(0, y), new(size.Width, y));
            return;
        }
        if (Kind is "BottomBar" or "SegmentedBar")
        {
            if (Kind == "SegmentedBar")
            {
                var width = Math.Max(0, (size.Width - 18) / 10);
                for (var i = 0; i < 10; i++) dc.DrawRectangle(i < Math.Round(level * 10) ? Fill : Track, null,
                    new Rect(i * (width + 2), (size.Height - 6) / 2, width, 6));
            }
            else
            {
                var bar = new Rect(0, (size.Height - 5) / 2, size.Width, 5);
                dc.DrawRectangle(Track, null, bar, 2.5, 2.5);
                if (level > 0) dc.DrawRectangle(Fill, null, new Rect(bar.X, bar.Y, Math.Max(5, size.Width * level), 5), 2.5, 2.5);
            }
            var x = Math.Clamp(size.Width * peak, 1, size.Width - 1);
            dc.DrawLine(mark, new(x, 0), new(x, size.Height));
            return;
        }
        var thickness = Kind == "Needle" ? 6d : 8d;
        var center = new Point(size.Width / 2, size.Height - (Kind == "Needle" ? 4 : 0));
        var across = Math.Max(1, size.Width / 2 - thickness / 2 - 1);
        var up = Math.Max(1, center.Y - thickness / 2 - 1);
        var rx = Kind == "Needle" ? Math.Min(across, up) : Math.Min(across, up * 1.2);
        var ry = Kind == "Needle" ? Math.Min(across, up) : up;
        Point At(double xRadius, double yRadius, double fraction) => new(center.X + xRadius * Math.Cos(Math.PI * (1 - fraction)),
            center.Y - yRadius * Math.Sin(Math.PI * (1 - fraction)));
        Geometry Arc(double from, double to)
        {
            var geometry = new StreamGeometry();
            using var path = geometry.Open();
            path.BeginFigure(At(rx, ry, from), false);
            path.ArcTo(At(rx, ry, to), new Size(rx, ry), 0, false, SweepDirection.Clockwise);
            path.EndFigure(false);
            return geometry;
        }
        if (Kind == "SegmentedArc")
        {
            var gap = 1 / (Math.PI * rx);
            for (var i = 0; i < 10; i++) dc.DrawGeometry(null, new Pen(i < Math.Round(level * 10) ? Fill : Track, thickness),
                Arc(i / 10d + gap, (i + 1) / 10d - gap));
        }
        else
        {
            dc.DrawGeometry(null, new Pen(Track, thickness), Arc(0, 1));
            if (level > 0) dc.DrawGeometry(null, new Pen(Fill, thickness), Arc(0, level));
        }
        dc.DrawLine(mark, At(rx - thickness / 2 - 2, ry - thickness / 2 - 2, peak),
            At(rx + thickness / 2 + 1, ry + thickness / 2 + 1, peak));
        if (Kind == "Needle")
        {
            dc.DrawLine(new Pen(Fill, 3, lineCap: PenLineCap.Round), center, At(rx - thickness / 2 - 1, ry - thickness / 2 - 1, level));
            dc.DrawEllipse(Fill, null, center, 4, 4);
        }
    }
}
