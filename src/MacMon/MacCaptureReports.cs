using System.Globalization;
using System.Text;
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
    public override string ToString() => $"{Peak.Timestamp.ToLocalTime():HH:mm:ss} · {Label} · {State}";
}

public static class MacCaptureReports
{
    public const string OwnershipMarker = "MacMon automatic capture v1";
    public static MacCaptureRecord Create(CaptureFired capture, string directory)
    {
        var id = Guid.NewGuid();
        var unit = capture.Rule.Metric == CaptureMetric.Network ? "Mbps" : capture.Rule.Metric == CaptureMetric.Process ? "% of one core" : "%";
        var label = $"{capture.Rule.Metric} · peak {capture.PeakValue:F1} {unit}";
        return new(id, SampleKey.Of(capture.Peak), capture.Rule.Metric, label,
            Path.Combine(directory, $"MacMon-capture-{capture.Peak.Timestamp.UtcDateTime:yyyyMMddTHHmmssfffZ}-{id:N}.txt"));
    }

    public static string AutomaticText(CaptureFired capture)
    {
        var sort = capture.Rule.Metric == CaptureMetric.Memory ? ProcessSort.Memory : ProcessSort.Cpu;
        var rows = capture.Peak.Processes.OrderByDescending(process => sort == ProcessSort.Memory
            ? process.WorkingSetBytes ?? 0 : process.ObservedCpuPercent ?? -1).Take(20).ToArray();
        return OwnershipMarker + "\n" +
            $"Rule: {capture.Rule.Metric} above {capture.Rule.Threshold.ToString(CultureInfo.InvariantCulture)} for {capture.Rule.Seconds} consecutive readings\n" +
            $"Peak value: {capture.PeakValue.ToString("F2", CultureInfo.InvariantCulture)}\n" +
            (capture.Process is { } process ? $"Triggering process: {Clean(process.Name)} · PID {process.ProcessId}\n" : "") +
            Text(capture.Peak, rows, "Top 20 observed processes at the peak; interval readings, without five-second averaging.");
    }

    public static string Text(MonitorSample sample, IReadOnlyList<ProcessUsage> rows, string processNote)
    {
        var r = sample.Resources;
        var text = new StringBuilder();
        text.AppendLine($"MacMon capture · {sample.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz} ({TimeZoneInfo.Local.Id})");
        text.AppendLine($"UTC: {sample.Timestamp.ToUniversalTime():O}");
        text.AppendLine($"CPU: {Number(sample.TotalCpuPercent)}% of machine capacity");
        text.AppendLine($"Memory estimate: {Number(r.MemoryPercent)}% · {r.UsedMemoryBytes?.ToString(CultureInfo.InvariantCulture) ?? "—"} bytes used");
        text.AppendLine($"Network {Clean(r.NetworkName)}: receive {Number(r.ReceiveBytesPerSecond)} B/s · send {Number(r.SendBytesPerSecond)} B/s");
        text.AppendLine($"All block-storage drivers: read {Number(r.SecondaryDiskReadBytesPerSecond)} B/s · write {Number(r.SecondaryDiskWriteBytesPerSecond)} B/s");
        text.AppendLine($"Data volume {Clean(r.DiskName)}: {r.DiskFreeBytes?.ToString(CultureInfo.InvariantCulture) ?? "—"} bytes available");
        text.AppendLine("— means unavailable. Process disk I/O excludes network traffic. Resident sums can count shared pages repeatedly.");
        text.AppendLine("Per-process network attribution is unavailable; the rows below do not identify network usage.");
        text.AppendLine(processNote);
        text.AppendLine("Name\tPID\tCPU % machine\tResident bytes\tDisk I/O B/s\tThreads");
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
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            { writer.Write(text); writer.Flush(); stream.Flush(true); }
            File.Move(temporary, record.ReportPath);
            Prune(directory, retain);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Prune(string directory, int retain)
    {
        var owned = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, "MacMon-capture-*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var suffix = name[(name.LastIndexOf('-') + 1)..];
            if (!Guid.TryParseExact(suffix, "N", out _) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            using var reader = File.OpenText(path);
            if (reader.ReadLine() == OwnershipMarker) owned.Add(path);
        }
        foreach (var path in owned.OrderDescending(StringComparer.Ordinal).Skip(Math.Max(1, retain))) File.Delete(path);
    }

    private static string Number(double? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "—";
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
