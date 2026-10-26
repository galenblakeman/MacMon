using Avalonia;
using WinMon.Monitoring;

namespace MacMon;

public enum ResourceChartMode { Overlay, SmallMultiples, Heatmap, Focus }
public sealed record ChartPanel(Rect Bounds, int[] Metrics, bool SideLabel = false);

/// <summary>Shared chart semantics: rates never become utilization percentages.</summary>
public static class ResourceCharts
{
    public static readonly string[] Names = ["CPU", "Memory", "Network", "Disk", "GPU"];
    public static bool IsPercent(int metric) => metric is 0 or 1 or 4;
    public static int[] Metrics(int mask) => new[] { 0, 4, 1, 3, 2 }.Where(i => (mask & (1 << i)) != 0).ToArray();
    public static int Focus(int mask, int focus) => Metrics(mask).Contains(focus) ? focus : Metrics(mask).FirstOrDefault();
    public static double? Value(MonitorSample sample, int metric)
    {
        if (sample.IsGap && metric != 1) return null;
        var r = sample.Resources;
        double? value = metric switch
        {
            0 => sample.TotalCpuPercent, 1 => r.MemoryPercent,
            2 => r.ReceiveBytesPerSecond,
            3 => r.SecondaryDiskReadBytesPerSecond + r.SecondaryDiskWriteBytesPerSecond,
            4 => r.GpuPercent, _ => null
        };
        return value is { } n && double.IsFinite(n) && n >= 0 ? Math.Min(n, IsPercent(metric) ? 100 : double.MaxValue) : null;
    }
    public static double Maximum(IReadOnlyList<MonitorSample> samples, int rangeSeconds, int metric) => IsPercent(metric) ? 100 :
        ResourceGaugeHistory.RateMaximum(HistoryInspection.VisibleSamples(samples, rangeSeconds).Select(s => Value(s, metric) ?? 0).DefaultIfEmpty().Max());
    public static string Reading(MonitorSample sample, int metric) => Value(sample, metric) is not { } value ? "—" :
        IsPercent(metric) ? $"{value:F1}%" : metric == 2 ? ResourceGaugeHistory.NetworkRate(value) : ResourceGaugeHistory.Rate(value);
    public static string Describe(MonitorSample sample) => $"{sample.Timestamp:HH:mm:ss zzz} · " +
        string.Join(" · ", Enumerable.Range(0, Names.Length).Select(i => $"{Names[i]} {Reading(sample, i)}")) + (sample.IsGap ? " · interval gap" : "");
    public static string Scale(int metric, double maximum) => IsPercent(metric) ? "0–100%" : $"0–{(metric == 2 ? ResourceGaugeHistory.NetworkRate(maximum) : ResourceGaugeHistory.Rate(maximum))}";
    public static ChartPanel[] Panels(Size size, int mask, ResourceChartMode mode, int focus)
    {
        var metrics = Metrics(mask);
        if (metrics.Length == 0 || size.Width <= 0 || size.Height <= 0) return [];
        if (mode == ResourceChartMode.Overlay) return [new(new(size), metrics)];
        if (mode == ResourceChartMode.Focus)
        {
            var selected = Focus(mask, focus);
            var others = metrics.Where(i => i != selected).ToArray();
            if (others.Length == 0) return [new(new(size), [selected])];
            var top = new ChartPanel(new(0, 0, size.Width, size.Height * .55), [selected]);
            var height = size.Height * .41 / others.Length;
            return [top, .. others.Select((metric, i) => new ChartPanel(new(0, size.Height * .59 + i * height, size.Width, height), [metric], true))];
        }
        var laneHeight = size.Height / metrics.Length;
        return metrics.Select((metric, i) => new ChartPanel(new(0, i * laneHeight, size.Width, laneHeight), [metric], true)).ToArray();
    }
    public static Rect Plot(ChartPanel panel) => panel.SideLabel
        ? new(panel.Bounds.X + 92, panel.Bounds.Y + 3, Math.Max(0, panel.Bounds.Width - 92), Math.Max(0, panel.Bounds.Height - 6))
        : panel.Bounds;
}
