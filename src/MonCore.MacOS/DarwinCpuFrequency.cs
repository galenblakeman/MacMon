using System.Buffers.Binary;
using System.Runtime.InteropServices;
using static MonCore.MacOS.DarwinObjects;

namespace MonCore.MacOS;

internal sealed record CpuFrequencyChannel(string Name, string[] States, long[] Residency);

/// <summary>Optional Apple Silicon requested-state frequency estimate. No root, model clock constants or idle floor.</summary>
internal sealed partial class DarwinCpuFrequency : IDisposable
{
    private const string IOReport = "/usr/lib/libIOReport.dylib";
    private IntPtr _subscription, _channels;
    private readonly Dictionary<char, double[]> _tables = [];
    private CpuFrequencyChannel[]? _previous;
    private bool _disabled;

    internal DarwinCpuFrequency()
    {
        try
        {
            // Only known two-tier layouts are decoded. Future/VM layouts remain unavailable.
            var brand = ReadBrand();
            var generation = System.Text.RegularExpressions.Regex.Match(brand, @"^Apple M([1-4])(?: |$)");
            if (!generation.Success) { _disabled = true; return; }
            var divisor = generation.Groups[1].Value == "4" ? 1000d : 1000000d;
            var tables = Services("AppleARMIODevice", entry =>
                (E: Property(entry, "voltage-states1-sram", Data), P: Property(entry, "voltage-states5-sram", Data)));
            foreach (var pair in tables)
            {
                if (DecodeTable(pair.E, divisor) is { Length: > 0 } e) _tables['E'] = e;
                if (DecodeTable(pair.P, divisor) is { Length: > 0 } p) _tables['P'] = p;
            }
            if (_tables.Count != 2) { _disabled = true; return; }
            var group = Key("CPU Stats"); var subgroup = Key("CPU Core Performance States");
            IntPtr requested = IntPtr.Zero;
            try
            {
                requested = IOReportCopyChannelsInGroup(group, subgroup, 0, 0, 0);
                if (requested != IntPtr.Zero) _subscription = IOReportCreateSubscription(IntPtr.Zero, requested, out _channels, 0, IntPtr.Zero);
            }
            finally { Release(requested); Release(group); Release(subgroup); }
            _disabled = _subscription == IntPtr.Zero || _channels == IntPtr.Zero;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { _disabled = true; Dispose(); }
    }
    internal double? Read(bool intervalValid, int logicalCores)
    {
        if (_disabled) return null;
        IntPtr sample = IntPtr.Zero;
        try
        {
            sample = IOReportCreateSamples(_subscription, _channels, IntPtr.Zero);
            if (sample == IntPtr.Zero) { _previous = null; return null; }
            var current = Array(Get(sample, "IOReportChannels")).Select(channel =>
            {
                var count = IOReportStateGetCount(channel);
                if (count is < 1 or > 64) return new CpuFrequencyChannel("", [], []);
                return new CpuFrequencyChannel(Text(IOReportChannelGetChannelName(channel)),
                    Enumerable.Range(0, count).Select(i => Text(IOReportStateGetNameForIndex(channel, i))).ToArray(),
                    Enumerable.Range(0, count).Select(i => IOReportStateGetResidency(channel, i)).ToArray());
            }).ToArray();
            var prior = _previous; _previous = current;
            return intervalValid && prior is not null ? Calculate(prior, current, _tables, logicalCores) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        { _disabled = true; _previous = null; return null; }
        finally { Release(sample); }
    }
    internal static double[] DecodeTable(byte[] data, double divisor)
    {
        if (data.Length == 0 || data.Length % 8 != 0 || data.Length > 512 || divisor is not (1000 or 1000000)) return [];
        var values = Enumerable.Range(0, data.Length / 8).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 8, 4)) / divisor).ToArray();
        return values.All(v => v is >= 100 and <= 10000) && values.SequenceEqual(values.Order()) ? values : [];
    }
    internal static double? Calculate(CpuFrequencyChannel[] before, CpuFrequencyChannel[] after, IReadOnlyDictionary<char, double[]> tables, int cores)
    {
        if (cores <= 0 || before.Length != cores || after.Length != cores || before.Select(c => c.Name).Distinct().Count() != cores || after.Select(c => c.Name).Distinct().Count() != cores) return null;
        double weighted = 0, active = 0;
        foreach (var channel in after)
        {
            var prior = before.FirstOrDefault(c => c.Name == channel.Name);
            var tier = channel.Name.StartsWith("ECPU", StringComparison.Ordinal) ? 'E' : channel.Name.StartsWith("PCPU", StringComparison.Ordinal) ? 'P' : '?';
            if (prior is null || !tables.TryGetValue(tier, out var frequencies) || !prior.States.SequenceEqual(channel.States) ||
                prior.Residency.Length != channel.States.Length || channel.Residency.Length != channel.States.Length) return null;
            var offset = channel.States.TakeWhile(s => s is "IDLE" or "DOWN" or "OFF").Count();
            if (offset == 0 || channel.States.Length - offset != frequencies.Length) return null;
            for (var i = 0; i < channel.States.Length; i++)
            {
                if (prior.Residency[i] < 0 || channel.Residency[i] < prior.Residency[i]) return null;
                var delta = (double)channel.Residency[i] - prior.Residency[i];
                if (i >= offset) { active += delta; weighted += delta * frequencies[i - offset]; }
            }
        }
        return active > 0 && double.IsFinite(weighted / active) ? weighted / active : null;
    }
    private static string ReadBrand()
    {
        var buffer = Marshal.AllocHGlobal(256);
        try { nuint length = 256; return sysctlbyname("machdep.cpu.brand_string", buffer, ref length, IntPtr.Zero, 0) == 0 && length is > 0 and <= 256 ? Marshal.PtrToStringUTF8(buffer) ?? "" : ""; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose() { Release(_channels); Release(_subscription); _channels = _subscription = IntPtr.Zero; _disabled = true; _previous = null; }
    [LibraryImport("/usr/lib/libSystem.B.dylib", StringMarshalling = StringMarshalling.Utf8)] private static partial int sysctlbyname(string name, IntPtr value, ref nuint size, IntPtr newValue, nuint newSize);
    [LibraryImport(IOReport)] private static partial IntPtr IOReportCopyChannelsInGroup(IntPtr group, IntPtr subgroup, ulong a, ulong b, ulong c);
    [LibraryImport(IOReport)] private static partial IntPtr IOReportCreateSubscription(IntPtr allocator, IntPtr channels, out IntPtr subscribed, ulong options, IntPtr unknown);
    [LibraryImport(IOReport)] private static partial IntPtr IOReportCreateSamples(IntPtr subscription, IntPtr channels, IntPtr unknown);
    [LibraryImport(IOReport)] private static partial IntPtr IOReportChannelGetChannelName(IntPtr channel);
    [LibraryImport(IOReport)] private static partial int IOReportStateGetCount(IntPtr channel);
    [LibraryImport(IOReport)] private static partial IntPtr IOReportStateGetNameForIndex(IntPtr channel, int index);
    [LibraryImport(IOReport)] private static partial long IOReportStateGetResidency(IntPtr channel, int index);
}
