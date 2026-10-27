using System.Diagnostics;
using WinMon.Monitoring;

namespace MacMon;

public sealed class HistoryInspection
{
    private MonitorSample? _selected;
    public bool IsHistorical { get; private set; }
    public SampleKey? SelectedKey => _selected is null ? null : SampleKey.Of(_selected);
    public void Live() { IsHistorical = false; _selected = null; }
    public void Select(MonitorSample sample) { IsHistorical = true; _selected = sample; }

    public IReadOnlyList<MonitorSample> DisplayHistory(IReadOnlyList<MonitorSample> retained, MonitorSample? full)
    {
        if (!IsHistorical) return full is null ? retained : [.. retained.SkipLast(1), full];
        var index = -1;
        for (var i = 0; i < retained.Count; i++)
            if (retained[i].MonotonicTimestamp == _selected?.MonotonicTimestamp && retained[i].Timestamp == _selected.Timestamp)
            { index = i; break; }
        return index < 0 ? [] : retained.Take(index + 1).ToArray();
    }

    public static IReadOnlyList<MonitorSample> VisibleSamples(IReadOnlyList<MonitorSample> samples, int rangeSeconds)
    {
        if (samples.Count == 0) return [];
        var end = samples[^1].MonotonicTimestamp;
        return samples.Where(sample => (end - sample.MonotonicTimestamp) / (double)Stopwatch.Frequency <= rangeSeconds).ToArray();
    }

    public static MonitorSample? Navigate(IReadOnlyList<MonitorSample> samples, int rangeSeconds, long? selected, int offset)
    {
        var visible = VisibleSamples(samples, rangeSeconds);
        if (visible.Count == 0) return null;
        var index = selected is { } time
            ? Enumerable.Range(0, visible.Count).MinBy(i => Math.Abs((double)visible[i].MonotonicTimestamp - time))
            : visible.Count - 1;
        return visible[(int)Math.Clamp((long)index + offset, 0, visible.Count - 1)];
    }

    public static MonitorSample? Nearest(IReadOnlyList<MonitorSample> samples, int rangeSeconds, double fraction)
    {
        if (samples.Count == 0 || !double.IsFinite(fraction)) return null;
        var end = samples[^1].MonotonicTimestamp;
        var target = end - (1 - Math.Clamp(fraction, 0, 1)) * rangeSeconds * Stopwatch.Frequency;
        return samples.Where(sample => (end - sample.MonotonicTimestamp) / (double)Stopwatch.Frequency <= rangeSeconds)
            .MinBy(sample => Math.Abs(sample.MonotonicTimestamp - target));
    }
}
