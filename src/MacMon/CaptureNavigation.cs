using WinMon.Monitoring;

namespace MacMon;

public static class CaptureNavigation
{
    public static MacCaptureRecord? Next(IReadOnlyList<MacCaptureRecord> captures, IReadOnlyList<MonitorSample> samples,
        long? selected, bool forward)
    {
        if (samples.Count == 0) return null;
        var time = samples.LastOrDefault(s => s.MonotonicTimestamp == selected)?.Timestamp ?? samples[^1].Timestamp;
        // Retained peaks are keyed by both clocks. Expired peaks can still open their saved report.
        var ordered = captures.OrderBy(c => c.Peak.Timestamp);
        return forward ? ordered.FirstOrDefault(c => c.Peak.Timestamp > time)
            : ordered.LastOrDefault(c => c.Peak.Timestamp < time);
    }
}
