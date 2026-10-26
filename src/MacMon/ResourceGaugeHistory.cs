using System.Diagnostics;
using WinMon.Monitoring;

namespace MacMon;

public sealed record GaugeReading(double? Value, double? Peak, double Maximum, string Text, string Scale)
{
    public static GaugeReading Empty { get; } = new(null, null, 100, "—", "Unavailable");
}

/// <summary>Only fresh samples enter this window; independent of persisted or inspected history.</summary>
public sealed class ResourceGaugeHistory
{
    private readonly Queue<(long Time, double?[] Values)> _samples = new();
    private string? _networkId;
    private string? _storageId;
    private string? _gpuId;
    private string? _secondDiskId;
    private long _last;
    public GaugeReading[] Readings { get; private set; } = Enumerable.Repeat(GaugeReading.Empty, ResourceCharts.Names.Length).ToArray();
    public void Observe(MonitorSample sample)
    {
        var r = sample.Resources;
        var now = sample.MonotonicTimestamp;
        if (now <= _last || (now - _last) / (double)Stopwatch.Frequency > 2.5) _samples.Clear();
        _last = now;
        var values = new[] { Valid(sample.TotalCpuPercent, 100), Valid(r.MemoryPercent, 100),
            Valid(r.ReceiveBytesPerSecond), Valid(sample.IsGap ? null : r.DiskActivePercent, 100), Valid(r.GpuPercent, 100), Valid(sample.IsGap ? null : r.SecondaryDiskActivePercent, 100) };
        // A baseline or source change starts a new activity interval, without retaining another device's peak.
        var primaryId = ResourceCharts.LegacyDisk(r) ? r.SecondaryDiskName : r.DiskName;
        foreach (var old in _samples)
        {
            if (_gpuId != r.GpuId || values[4] is null) old.Values[4] = null;
            if (_networkId != r.NetworkId || values[2] is null) old.Values[2] = null;
            if (_secondDiskId != r.SecondaryDiskName || values[5] is null) old.Values[5] = null;
            if (_storageId != primaryId || values[3] is null) old.Values[3] = null;
        }
        _gpuId = r.GpuId;
        _networkId = r.NetworkId; _storageId = primaryId; _secondDiskId = r.SecondaryDiskName;
        _samples.Enqueue((now, values));
        while (_samples.Count > 256 || _samples.TryPeek(out var first) && (now - first.Time) / (double)Stopwatch.Frequency > 120)
            _samples.Dequeue();
        Readings = Enumerable.Range(0, ResourceCharts.Names.Length).Select(index =>
        {
            if (values[index] is not { } value) return GaugeReading.Empty;
            var peak = _samples.Max(entry => entry.Values[index]) ?? value;
            var maximum = IsPercent(index) ? 100 : index == 2 ? NetworkMaximum(peak) : RateMaximum(peak);
            return new GaugeReading(value, peak, maximum, IsPercent(index) ? $"{value:F1}%" : index == 2 ? NetworkRate(value) : Rate(value),
                IsPercent(index) ? "0–100%" : $"0–{(index == 2 ? NetworkRate(maximum) : Rate(maximum))}");
        }).ToArray();
    }
    public static bool IsPercent(int metric) => metric is 0 or 1 or 3 or 4 or 5;
    private static double? Valid(double? value, double maximum = double.MaxValue) =>
        value is { } number && double.IsFinite(number) && number >= 0 ? Math.Min(number, maximum) : null;
    private static double? Total(double? left, double? right) => Valid(left) is { } a && Valid(right) is { } b ? Valid(a + b) : null;
    public static double RateMaximum(double peak)
    {
        var scale = 1024d;
        while (scale < peak && scale < double.MaxValue / 2) scale *= 2;
        return Math.Max(peak, scale);
    }
    public static double NetworkMaximum(double bytesPerSecond)
    {
        var bits = Math.Max(bytesPerSecond * 8, 1_000_000);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(bits)));
        var step = bits / magnitude;
        return (step <= 1 ? 1 : step <= 2 ? 2 : step <= 5 ? 5 : 10) * magnitude / 8;
    }
    public static string NetworkScaleLabel(double maximum)
    {
        var bits = maximum * 8;
        return bits >= 1e9 ? $"{bits / 1e9:F1}G" : $"{bits / 1e6:F1}M";
    }
    public static string Rate(double? value) => value is not { } bytes ? "—" : bytes >= 1073741824 ? $"{bytes / 1073741824:F1} GiB/s" :
        bytes >= 1048576 ? $"{bytes / 1048576:F1} MiB/s" : $"{bytes / 1024:F1} KiB/s";
    public static string NetworkRate(double? value) => value is not { } bytes || !double.IsFinite(bytes) || bytes < 0 ? "—" :
        bytes >= 125000000 ? $"{bytes / 125000000:F1} Gbps" : bytes >= 125000 ? $"{bytes / 125000:F1} Mbps" : $"{bytes / 125:F1} Kbps";
}
