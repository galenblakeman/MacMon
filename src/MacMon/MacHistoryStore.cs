using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using WinMon.Monitoring;

namespace MacMon;

public sealed record MacHistoryEntry(MonitorSample Sample, string? BootId);
public sealed record HistoryLoadResult(IReadOnlyList<MacHistoryEntry> Entries, long ClockOffset = 0,
    IReadOnlyDictionary<SampleKey, SampleKey>? RebasedKeys = null, string? Error = null, bool CanSave = true)
{
    public SampleKey ResolvePeak(SampleKey original)
    {
        if (RebasedKeys is not null && RebasedKeys.TryGetValue(original, out var key)) return key;
        // Report metadata keeps its original clock. History can have been rebased and saved repeatedly.
        var matches = Entries.Where(entry => entry.Sample.Timestamp == original.Timestamp).Take(2).ToArray();
        return matches.Length == 1 ? SampleKey.Of(matches[0].Sample) : new SampleKey(0, original.Timestamp);
    }
}

/// <summary>Mac-only versioned history. No Windows restart serializer or clock-domain assumptions.</summary>
public static class MacHistoryStore
{
    private const int Magic = 0x31484D4D; // MMH1, little endian
    private const int End = 0x21444E45;
    public const int MaxSamples = 3600;
    public const int MaxProcesses = 500_000;
    private const long MaxFileBytes = 64L << 20;
    private const long MaxExpandedBytes = 192L << 20;

    public static void Save(string path, IReadOnlyList<MacHistoryEntry> entries, long frequency = 0)
    {
        frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
        if (frequency <= 0 || entries.Count > MaxSamples || entries.Sum(entry => (long)entry.Sample.Processes.Count) > MaxProcesses)
            throw new InvalidDataException("History exceeds its bounds.");
        var strings = new Dictionary<string, int>(StringComparer.Ordinal) { [""] = 0 };
        void Add(string? text) { if (text is not null && !strings.ContainsKey(text)) strings.Add(text, strings.Count); }
        foreach (var entry in entries)
        {
            Add(entry.BootId);
            var resource = entry.Sample.Resources;
            Add(resource.DiskName); Add(resource.NetworkName); Add(resource.NetworkId); Add(resource.SecondaryDiskName);
            Add(resource.GpuId); Add(resource.GpuName); Add(resource.GpuEngine);
            foreach (var process in entry.Sample.Processes) { Add(process.Name); Add(process.ImagePath); }
        }
        if (strings.Count > 65_536) throw new InvalidDataException("Too many history strings.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            {
                using (var zip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true))
                using (var bounded = new BudgetStream(zip, MaxExpandedBytes))
                using (var writer = new BinaryWriter(bounded, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(Magic); writer.Write(2); writer.Write(frequency);
                    writer.Write(strings.Count);
                    var stringBytes = 0;
                    foreach (var text in strings.Keys)
                    {
                        var bytes = Encoding.UTF8.GetBytes(text);
                        stringBytes = checked(stringBytes + bytes.Length);
                        if (bytes.Length > 16384 || stringBytes > 16 << 20) throw new InvalidDataException("History strings exceed their bounds.");
                        writer.Write(bytes.Length); writer.Write(bytes);
                    }
                    writer.Write(entries.Count);
                    long previous = 0;
                    foreach (var entry in entries)
                    {
                        var sample = entry.Sample;
                        if (sample.MonotonicTimestamp <= previous) throw new InvalidDataException("History is not chronological.");
                        previous = sample.MonotonicTimestamp;
                        writer.Write(strings[entry.BootId ?? ""]);
                        WriteTime(writer, sample.Timestamp); writer.Write(sample.MonotonicTimestamp);
                        WriteNumber(writer, sample.TotalCpuPercent);
                        writer.Write(sample.UnavailableProcesses); writer.Write(sample.CompactedProcesses);
                        writer.Write(sample.IsGap); writer.Write(sample.IndependentProcessReadings);
                        writer.Write(sample.IntervalSeconds); writer.Write(sample.IntervalCpuTicks);
                        WriteResources(writer, sample.Resources, strings);
                        writer.Write(sample.Processes.Count);
                        foreach (var process in sample.Processes)
                        {
                            if (process.LifetimeDomain != ProcessLifetimeDomain.UnixMicroseconds)
                                throw new InvalidDataException("Only Mac Unix-microsecond process identities can be saved.");
                            writer.Write(process.ProcessId); writer.Write(process.CreationTicks);
                            writer.Write(process.StartedAtUtc is not null);
                            if (process.StartedAtUtc is { } start) WriteTime(writer, start);
                            writer.Write(strings[process.Name]); writer.Write(strings[process.ImagePath]);
                            writer.Write(process.ParentProcessId); writer.Write(process.CpuAvailable);
                            WriteNumber(writer, process.ObservedCpuPercent);
                            WriteUnsigned(writer, process.WorkingSetBytes); WriteNumber(writer, process.IoBytesPerSecond);
                            writer.Write(process.ThreadCount is not null);
                            if (process.ThreadCount is { } threads) writer.Write(threads);
                        }
                    }
                    writer.Write(End); writer.Flush();
                }
                if (file.Length > MaxFileBytes) throw new InvalidDataException("Compressed history exceeds the size limit.");
                file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static HistoryLoadResult Load(string path, int retentionSeconds, DateTimeOffset now, long monotonicNow, long frequency = 0)
    {
        frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
        if (retentionSeconds is < 1 or > MaxSamples || frequency <= 0 || monotonicNow < 0) throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
        if (!File.Exists(path)) return new([]);
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("History exceeds the file size limit.");
            using var file = File.OpenRead(path);
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            using var bounded = new BudgetStream(zip, MaxExpandedBytes);
            using var reader = new BinaryReader(bounded, Encoding.UTF8);
            if (reader.ReadInt32() != Magic) throw new InvalidDataException("Not a MacMon history file.");
            var version = reader.ReadInt32();
            if (version is not (1 or 2)) return new([], Error: "History uses an unsupported version; the original file will be preserved.", CanSave: false);
            var savedFrequency = reader.ReadInt64();
            if (savedFrequency <= 0) throw new InvalidDataException("Invalid saved clock frequency.");
            var strings = new string[Count(reader, 65_536)];
            var stringBytes = 0;
            for (var i = 0; i < strings.Length; i++)
            {
                var length = Count(reader, 16384);
                stringBytes = checked(stringBytes + length);
                if (stringBytes > 16 << 20) throw new InvalidDataException("History strings exceed the size limit.");
                var bytes = reader.ReadBytes(length);
                if (bytes.Length != length) throw new EndOfStreamException();
                strings[i] = new UTF8Encoding(false, true).GetString(bytes);
            }
            string Text() { var index = Count(reader, strings.Length - 1); return strings[index]; }
            var entries = new List<MacHistoryEntry>();
            var samples = Count(reader, MaxSamples);
            var remainingProcesses = MaxProcesses;
            long previous = 0;
            for (var i = 0; i < samples; i++)
            {
                var boot = Text(); var timestamp = ReadTime(reader); var moment = reader.ReadInt64();
                if (moment <= previous) throw new InvalidDataException("History is not chronological.");
                previous = moment;
                var cpu = ReadNumber(reader);
                if (cpu > 100) throw new InvalidDataException("Invalid machine CPU.");
                var unavailable = Count(reader, 1_000_000); var compacted = Count(reader, 1_000_000);
                var gap = reader.ReadBoolean(); var independent = reader.ReadBoolean();
                var seconds = reader.ReadDouble(); var capacity = reader.ReadInt64();
                if (!double.IsFinite(seconds) || seconds is < 0 or > 5 || capacity < 0) throw new InvalidDataException("Invalid sample interval.");
                var resources = ReadResources(reader, Text, version);
                var processes = new ProcessUsage[Count(reader, remainingProcesses)];
                remainingProcesses -= processes.Length;
                var identities = new HashSet<ProcessLifetimeIdentity>();
                for (var j = 0; j < processes.Length; j++)
                {
                    var pid = reader.ReadInt32(); var token = reader.ReadInt64();
                    DateTimeOffset? start = reader.ReadBoolean() ? ReadTime(reader) : null;
                    var name = Text(); var image = Text(); var parent = reader.ReadInt32();
                    var available = reader.ReadBoolean(); var processCpu = ReadNumber(reader);
                    var resident = ReadUnsigned(reader); var io = ReadNumber(reader);
                    long? threads = reader.ReadBoolean() ? reader.ReadInt64() : null;
                    if (pid <= 0 || token <= 0 || parent < 0 || processCpu > 100 || available != (processCpu is not null) || threads < 0)
                        throw new InvalidDataException("Invalid process observation.");
                    var process = new ProcessUsage(pid, token, name, processCpu ?? 0, resident, io)
                    {
                        LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, StartedAtUtc = start,
                        ImagePath = image, ParentProcessId = parent, CpuAvailable = available, ThreadCount = threads
                    };
                    if (!identities.Add(process.Identity)) throw new InvalidDataException("Duplicate process lifetime.");
                    processes[j] = process;
                }
                entries.Add(new(new(timestamp, cpu, processes, unavailable, gap, moment)
                {
                    IndependentProcessReadings = independent, CompactedProcesses = compacted,
                    IntervalSeconds = seconds, IntervalCpuTicks = capacity, Resources = resources
                }, boot.Length == 0 ? null : boot));
            }
            if (reader.ReadInt32() != End || bounded.ReadByte() != -1) throw new InvalidDataException("Invalid history footer.");
            if (entries.Count == 0) return new([]);
            var last = entries[^1].Sample;
            var sinceLast = (now - last.Timestamp).TotalSeconds;
            if (sinceLast < -5) return new([], Error: "Saved history is ahead of the current clock and was ignored.");
            sinceLast = Math.Max(0, sinceLast);
            var ages = entries.Select(entry => (Entry: entry, Age: sinceLast +
                (last.MonotonicTimestamp - entry.Sample.MonotonicTimestamp) / (double)savedFrequency))
                .Where(item => item.Age < retentionSeconds).ToArray();
            if (ages.Length == 0) return new([]);
            // After reboot, old history may predate this boot's uptime. Use a positive virtual clock for both
            // restored samples and future live frames rather than mixing negative ticks with wall-time fallbacks.
            var offset = Math.Max(0, checked((long)Math.Ceiling(ages[0].Age * frequency) + frequency - monotonicNow));
            var restored = new List<MacHistoryEntry>();
            var keys = new Dictionary<SampleKey, SampleKey>();
            foreach (var (entry, age) in ages)
            {
                var sample = entry.Sample with { MonotonicTimestamp = checked(monotonicNow + offset - (long)Math.Round(age * frequency)) };
                if (restored.Count > 0 && sample.MonotonicTimestamp <= restored[^1].Sample.MonotonicTimestamp)
                    throw new InvalidDataException("Saved intervals cannot be represented on this clock.");
                restored.Add(entry with { Sample = sample }); keys.Add(SampleKey.Of(entry.Sample), SampleKey.Of(sample));
            }
            return new(restored, offset, keys);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            var invalid = path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            try { File.Move(path, invalid); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            { return new([], Error: $"Could not read or preserve history: {ex.Message}", CanSave: false); }
            return new([], Error: $"Saved history was invalid and preserved at {invalid}");
        }
    }

    private static void WriteResources(BinaryWriter w, ResourceSample r, Dictionary<string, int> text)
    {
        if (r.GpuPercent > 100 || r.CpuClockMhz is <= 0 or > 10000) throw new InvalidDataException("Invalid hardware reading.");
        WriteNumber(w, r.CpuKernelPercent);
        WriteUnsigned(w, r.TotalMemoryBytes); WriteUnsigned(w, r.UsedMemoryBytes); WriteUnsigned(w, r.AvailableMemoryBytes); WriteUnsigned(w, r.CachedBytes);
        w.Write(r.ProcessCount is not null); if (r.ProcessCount is { } count) w.Write(count);
        w.Write(text[r.DiskName]); WriteUnsigned(w, r.DiskFreeBytes); WriteUnsigned(w, r.DiskTotalBytes);
        w.Write(text[r.NetworkId]); w.Write(text[r.NetworkName]); WriteNumber(w, r.ReceiveBytesPerSecond); WriteNumber(w, r.SendBytesPerSecond);
        w.Write(text[r.SecondaryDiskName]); WriteNumber(w, r.SecondaryDiskReadBytesPerSecond); WriteNumber(w, r.SecondaryDiskWriteBytesPerSecond);
        WriteNumber(w, r.CpuClockMhz); WriteNumber(w, r.GpuPercent);
        w.Write(text[r.GpuId]); w.Write(text[r.GpuName]); w.Write(text[r.GpuEngine]);
        WriteUnsigned(w, r.GpuSharedMemoryUsedBytes);
        WriteNumber(w, r.DiskReadBytesPerSecond); WriteNumber(w, r.DiskWriteBytesPerSecond);
        WriteUnsigned(w, r.SecondaryDiskTotalBytes);
    }
    private static ResourceSample ReadResources(BinaryReader r, Func<string> text, int version)
    {
        var result = new ResourceSample
        {
            CpuKernelPercent = ReadNumber(r), TotalMemoryBytes = ReadUnsigned(r), UsedMemoryBytes = ReadUnsigned(r),
            AvailableMemoryBytes = ReadUnsigned(r), CachedBytes = ReadUnsigned(r), ProcessCount = r.ReadBoolean() ? r.ReadUInt32() : null,
            DiskName = text(), DiskFreeBytes = ReadUnsigned(r), DiskTotalBytes = ReadUnsigned(r),
            NetworkId = text(), NetworkName = text(), ReceiveBytesPerSecond = ReadNumber(r), SendBytesPerSecond = ReadNumber(r),
            SecondaryDiskName = text(), SecondaryDiskReadBytesPerSecond = ReadNumber(r), SecondaryDiskWriteBytesPerSecond = ReadNumber(r)
        };
        if (version >= 2) result = result with
        {
            CpuClockMhz = ReadNumber(r), GpuPercent = ReadNumber(r), GpuId = text(), GpuName = text(), GpuEngine = text(),
            GpuSharedMemoryUsedBytes = ReadUnsigned(r), DiskReadBytesPerSecond = ReadNumber(r), DiskWriteBytesPerSecond = ReadNumber(r),
            SecondaryDiskTotalBytes = ReadUnsigned(r)
        };
        if (result.GpuPercent > 100 || result.CpuClockMhz is <= 0 or > 10000) throw new InvalidDataException("Invalid hardware reading.");
        if (result.CpuKernelPercent > 100 || result.UsedMemoryBytes > result.TotalMemoryBytes || result.AvailableMemoryBytes > result.TotalMemoryBytes)
            throw new InvalidDataException("Invalid resource observation.");
        return result;
    }
    private static void WriteTime(BinaryWriter writer, DateTimeOffset value) { writer.Write(value.Ticks); writer.Write((short)value.Offset.TotalMinutes); }
    private static DateTimeOffset ReadTime(BinaryReader reader) => new(reader.ReadInt64(), TimeSpan.FromMinutes(reader.ReadInt16()));
    private static void WriteNumber(BinaryWriter writer, double? value)
    { writer.Write(value is not null); if (value is { } number) { if (!double.IsFinite(number) || number < 0) throw new InvalidDataException("Invalid metric."); writer.Write(number); } }
    private static double? ReadNumber(BinaryReader reader)
    { if (!reader.ReadBoolean()) return null; var value = reader.ReadDouble(); return double.IsFinite(value) && value >= 0 ? value : throw new InvalidDataException("Invalid metric."); }
    private static void WriteUnsigned(BinaryWriter writer, ulong? value) { writer.Write(value is not null); if (value is { } number) writer.Write(number); }
    private static ulong? ReadUnsigned(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadUInt64() : null;
    private static int Count(BinaryReader reader, int maximum)
    { var count = reader.ReadInt32(); return count >= 0 && count <= maximum ? count : throw new InvalidDataException("History count exceeds its bounds."); }

    private sealed class BudgetStream(Stream inner, long maximum) : Stream
    {
        private long _bytes;
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        { var read = inner.Read(buffer); Account(read); return read; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { Account(buffer.Length); inner.Write(buffer); }
        private void Account(int count) { _bytes += count; if (_bytes > maximum) throw new InvalidDataException("Expanded history exceeds its size limit."); }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
