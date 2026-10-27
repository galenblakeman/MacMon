using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using WinMon.Monitoring;

namespace MacMon;

public static class MacPaths
{
    public static string AppSupport => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "MacMon");
}

public sealed record MacCaptureRecord(Guid Id, SampleKey Peak, CaptureMetric Metric, string Label, string ReportPath,
    string State = "Saving")
{
    public override string ToString() => $"{Peak.Timestamp.ToLocalTime():HH:mm:ss} · {Label} · {UiText.Get(State)}";
}

public static class MacCaptureReports
{
    public const string OwnershipMarker = "MacMon automatic capture v1";
    public static MacCaptureRecord Create(CaptureFired capture, string directory)
    {
        var id = Guid.NewGuid();
        var unit = capture.Rule.Metric == CaptureMetric.Network ? "Mbps" : capture.Rule.Metric == CaptureMetric.Process ? "% of one core" : "%";
        var metric = MetricLabel(capture.Rule.Metric);
        var label = UiText.Interpolate($"{metric} · peak {capture.PeakValue:F1} {UiText.Get(unit)}");
        return new(id, SampleKey.Of(capture.Peak), capture.Rule.Metric, label,
            Path.Combine(directory, $"MacMon-capture-{capture.Peak.Timestamp.UtcDateTime:yyyyMMddTHHmmssfffZ}-{id:N}.txt"));
    }

    public static string MetricLabel(CaptureMetric metric) => UiText.Get(metric switch
    { CaptureMetric.Cpu => "CPU", CaptureMetric.Memory => "Memory", CaptureMetric.Network => "Network",
      CaptureMetric.Gpu => "GPU", CaptureMetric.Process => "Any process", CaptureMetric.PrimaryDisk => "Disk · estimated I/O activity",
      CaptureMetric.SecondaryDisk => "Disk 2 · estimated I/O activity", _ => metric.ToString() });

    public static string AutomaticText(CaptureFired capture)
    {
        var sort = capture.Rule.Metric switch
        { CaptureMetric.Memory => ProcessSort.Memory, CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk => ProcessSort.Io, _ => ProcessSort.Cpu };
        var rows = capture.Peak.Processes.OrderByDescending(process => sort == ProcessSort.Memory
            ? process.WorkingSetBytes ?? 0 : sort == ProcessSort.Io ? process.IoBytesPerSecond ?? -1 : process.ObservedCpuPercent ?? -1).Take(20).ToArray();
        return OwnershipMarker + "\n" +
            UiText.Interpolate($"Rule: {MetricLabel(capture.Rule.Metric)} above {capture.Rule.Threshold.ToString("F1", CultureInfo.InvariantCulture)} for {capture.Rule.Seconds} consecutive readings\n") +
            (capture.Rule.Metric is CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk
                ? UiText.Get("Measurement: Estimated I/O activity (%); overlapping operation times, capped at 100. Not Windows active time.\nProcess I/O includes all devices and does not attribute activity to the selected disk.\n") : "") +
            UiText.Interpolate($"Peak value: {capture.PeakValue.ToString("F1", CultureInfo.InvariantCulture)}\n") +
            (capture.Process is { } process ? UiText.Interpolate($"Triggering process: {Clean(process.Name)} · PID {process.ProcessId}\n") : "") +
            Text(capture.Peak, rows, UiText.Get("Top 20 observed processes at the peak; interval readings, without five-second averaging."));
    }

    public static string Text(MonitorSample sample, IReadOnlyList<ProcessUsage> rows, string processNote)
    {
        var r = sample.Resources;
        var text = new StringBuilder();
        text.AppendLine(UiText.Interpolate($"MacMon capture · {sample.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz} ({TimeZoneInfo.Local.Id})"));
        text.AppendLine(UiText.Interpolate($"UTC: {sample.Timestamp.ToUniversalTime():O}"));
        text.AppendLine(UiText.Interpolate($"CPU: {Number(sample.TotalCpuPercent)}% of machine capacity"));
        text.AppendLine(UiText.Interpolate($"CPU clock estimate: {Number(r.CpuClockMhz)} MHz (requested-state residency)"));
        text.AppendLine(UiText.Interpolate($"GPU {Clean(r.GpuName)}: {Number(r.GpuPercent)}% device utilization"));
        text.AppendLine(UiText.Interpolate($"Memory estimate: {Number(r.MemoryPercent)}% · {r.UsedMemoryBytes?.ToString(CultureInfo.InvariantCulture) ?? "—"} bytes used"));
        text.AppendLine(UiText.Interpolate($"Network {Clean(r.NetworkName)}: receive {Number(r.ReceiveBytesPerSecond)} B/s · send {Number(r.SendBytesPerSecond)} B/s"));
        text.AppendLine(UiText.Interpolate($"Estimated disk I/O activity: primary {Number(r.DiskActivePercent)}% · second {Number(r.SecondaryDiskActivePercent)}% (overlapping operation durations; capped load estimate)"));
        var legacyDisk = ResourceCharts.LegacyDisk(r);
        text.AppendLine(UiText.Interpolate($"Primary disk {Clean(legacyDisk ? r.SecondaryDiskName : r.DiskName)}: read {Number(legacyDisk ? r.SecondaryDiskReadBytesPerSecond : r.DiskReadBytesPerSecond)} B/s · write {Number(legacyDisk ? r.SecondaryDiskWriteBytesPerSecond : r.DiskWriteBytesPerSecond)} B/s"));
        if (!legacyDisk && r.SecondaryDiskName.Length > 0)
            text.AppendLine(UiText.Interpolate($"Second disk {Clean(r.SecondaryDiskName)}: read {Number(r.SecondaryDiskReadBytesPerSecond)} B/s · write {Number(r.SecondaryDiskWriteBytesPerSecond)} B/s"));
        if (legacyDisk) text.AppendLine(UiText.Interpolate($"Data volume {Clean(r.DiskName)}: {r.DiskFreeBytes?.ToString(CultureInfo.InvariantCulture) ?? "—"} bytes available"));
        text.AppendLine(UiText.Get("— means unavailable. Process disk I/O excludes network traffic. Resident sums can count shared pages repeatedly."));
        text.AppendLine(UiText.Get("Per-process GPU attribution is unavailable; process rows do not identify GPU usage."));
        text.AppendLine(UiText.Get("Per-process network attribution is unavailable; the rows below do not identify network usage."));
        text.AppendLine(processNote);
        text.AppendLine(UiText.Get("Name\tPID\tCPU % machine\tResident bytes\tDisk I/O B/s\tThreads"));
        foreach (var row in rows)
            text.AppendLine($"{Clean(row.Name)}\t{row.ProcessId}\t{Number(row.ObservedCpuPercent)}\t{row.WorkingSetBytes?.ToString(CultureInfo.InvariantCulture) ?? "—"}\t{Number(row.IoBytesPerSecond)}\t{row.ThreadCount?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
        return text.ToString();
    }

    public static void Write(MacCaptureRecord record, string text, int retain = 200)
    {
        var directory = Path.GetDirectoryName(record.ReportPath)!;
        Directory.CreateDirectory(directory);
        var temporary = record.ReportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (text.StartsWith(OwnershipMarker + "\n", StringComparison.Ordinal))
                text = OwnershipMarker + "\nMacMon metadata: " + JsonSerializer.Serialize(record with { ReportPath = "", State = "Saved" }) +
                    "\n" + text[(OwnershipMarker.Length + 1)..];
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            { writer.Write(text); writer.Flush(); stream.Flush(true); }
            File.Move(temporary, record.ReportPath);
            Prune(directory, retain);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static IReadOnlyList<MacCaptureRecord> LoadRecords(string directory)
    {
        var records = new List<MacCaptureRecord>();
        if (!Directory.Exists(directory)) return records;
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "MacMon-capture-*.txt").OrderDescending(StringComparer.Ordinal).Take(500))
            {
                try
                {
                    if (new FileInfo(path).Length > 1_048_576 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    using var reader = File.OpenText(path);
                    if (reader.ReadLine() != OwnershipMarker || reader.ReadLine() is not { } metadata ||
                        !metadata.StartsWith("MacMon metadata: ", StringComparison.Ordinal)) continue;
                    var record = JsonSerializer.Deserialize<MacCaptureRecord>(metadata[17..]);
                    if (record is null || record.Id == Guid.Empty || record.Label is null || record.Label.Length > 256 ||
                        !MacCaptureOptions.SupportedMetrics.Contains(record.Metric)) continue;
                    var expected = $"MacMon-capture-{record.Peak.Timestamp.UtcDateTime:yyyyMMddTHHmmssfffZ}-{record.Id:N}.txt";
                    if (Path.GetFileName(path) != expected) continue;
                    records.Add(record with { ReportPath = path, State = "Saved" });
                    if (records.Count == 200) break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return records;
    }

    private static void Prune(string directory, int retain)
    {
        var owned = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "MacMon-capture-*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var suffix = name[(name.LastIndexOf('-') + 1)..];
            if (new FileInfo(path).Length > 1_048_576 || !Guid.TryParseExact(suffix, "N", out _) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            using var reader = File.OpenText(path);
            if (reader.ReadLine() == OwnershipMarker) owned.Add(path);
        }
        foreach (var path in owned.OrderDescending(StringComparer.Ordinal).Skip(Math.Max(1, retain))) File.Delete(path);
    }

    private static string Number(double? value) => value?.ToString("F1", CultureInfo.InvariantCulture) ?? "—";
    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}

/// <summary>A bounded file queue; sampling and the UI never wait for disk I/O. Shutdown drains accepted reports.</summary>
public sealed class CaptureReportWriter
{
    private readonly Channel<(MacCaptureRecord Record, string Text)> _queue = Channel.CreateBounded<(MacCaptureRecord, string)>(
        new BoundedChannelOptions(32) { SingleReader = true, SingleWriter = true });
    private readonly Task _worker;
    public CaptureReportWriter(Action<MacCaptureRecord, string?> completed)
    {
        _worker = Task.Run(async () =>
        {
            await foreach (var (record, text) in _queue.Reader.ReadAllAsync())
            {
                string? error = null;
                try { MacCaptureReports.Write(record, text); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; }
                completed(record, error);
            }
        });
    }
    public bool Enqueue(MacCaptureRecord record, string text) => _queue.Writer.TryWrite((record, text));
    public Task Complete() { _queue.Writer.TryComplete(); return _worker; }
}
