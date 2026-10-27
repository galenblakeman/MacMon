using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MonCore.MacOS;

public sealed record MacInventoryGpu(string? Model, string? Vendor, string? Bus, ulong? Cores, string? ReportedMemory);
public sealed record MacInventoryMemory(string? Slot, string? Size, string? Type, string? Speed, string? Manufacturer);
public sealed record MacInventoryDisk(string Name, ulong? Capacity);
public sealed record MacInventoryVolume(string? Name, string? Device, string? MountPoint, string? FileSystem,
    ulong? Capacity, ulong? Available, bool? Writable);
public sealed record MacInventoryNetwork(string Name, string Description, string Type, string Status, long? LinkSpeed,
    IReadOnlyList<string> Addresses);
public sealed record MacHardwareInventory(DateTimeOffset ObservedAt, string? Model, string? ModelIdentifier,
    string? OsVersion, string? OsBuild, string? Processor, ulong? PhysicalCores, ulong? LogicalCores, ulong? MemoryBytes,
    IReadOnlyList<MacInventoryMemory> Memory, IReadOnlyList<MacInventoryGpu> Graphics,
    IReadOnlyList<MacInventoryDisk> Disks, IReadOnlyList<MacInventoryVolume> Volumes,
    IReadOnlyList<MacInventoryNetwork> Networks, IReadOnlyList<string> UnavailableSections);

internal sealed record ProfilerInventory(string? Model, string? Processor, IReadOnlyList<MacInventoryMemory> Memory,
    IReadOnlyList<MacInventoryGpu> Graphics, IReadOnlyList<MacInventoryVolume> Volumes, IReadOnlyList<string> UnavailableSections);

/// <summary>On-demand specifications, independent of sampling and selected monitoring devices.</summary>
public sealed partial class MacHardwareInventoryReader
{
    internal const int MaximumCharacters = 2_097_152;
    internal const int MaximumDevices = 256;
    private readonly Func<CancellationToken, Task<string>> _profiler;
    public MacHardwareInventoryReader() : this(RunProfiler) { }
    internal MacHardwareInventoryReader(Func<CancellationToken, Task<string>> profiler) => _profiler = profiler;
    public async Task<MacHardwareInventory> Read(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Hardware inventory requires macOS.");
        cancellationToken.ThrowIfCancellationRequested();
        var profiler = await ReadProfiler(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unavailable = profiler.UnavailableSections.ToList();
            IReadOnlyList<MacInventoryDisk> disks = [];
            IReadOnlyList<MacInventoryNetwork> networks = [];
            try
            {
                var devices = DarwinStorageReader.Read().Devices;
                if (devices.Length > MaximumDevices || devices.Any(disk => !disk.Name.StartsWith("disk", StringComparison.Ordinal)))
                    unavailable.Add("Whole disk devices");
                disks = devices.Where(disk => System.Text.RegularExpressions.Regex.IsMatch(disk.Name, @"^disk[0-9]+ · "))
                    .Take(MaximumDevices)
                    .Select(disk => new MacInventoryDisk(disk.Name, disk.Size)).ToArray();
                if (disks.Count == 0) unavailable.Add("Whole disk devices");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unavailable.Add("Whole disk devices"); }
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces();
                if (adapters.Length > MaximumDevices) unavailable.Add("Network interfaces");
                networks = adapters.OrderBy(adapter => adapter.Name, StringComparer.Ordinal)
                    .Take(MaximumDevices).Select(Network).ToArray();
                if (networks.Count == 0) unavailable.Add("Network interfaces");
            }
            catch (NetworkInformationException) { unavailable.Add("Network interfaces"); }
            return new MacHardwareInventory(DateTimeOffset.Now, profiler.Model, SysctlText("hw.model"),
                SysctlText("kern.osproductversion"), SysctlText("kern.osversion"), profiler.Processor ?? SysctlText("machdep.cpu.brand_string"),
                SysctlNumber("hw.physicalcpu"), SysctlNumber("hw.logicalcpu"), SysctlNumber("hw.memsize"),
                profiler.Memory, profiler.Graphics, disks, profiler.Volumes, networks, unavailable.Distinct().ToArray());
        }, cancellationToken);
    }

    private static MacInventoryNetwork Network(NetworkInterface adapter)
    {
        string[] addresses = [];
        long? speed = null;
        try { addresses = adapter.GetIPProperties().UnicastAddresses.Select(address => address.Address.ToString()).Take(32).ToArray(); }
        catch (NetworkInformationException) { }
        try { if (adapter.Speed > 0) speed = adapter.Speed; }
        catch (NetworkInformationException) { }
        return new(adapter.Name, adapter.Description, adapter.NetworkInterfaceType.ToString(), adapter.OperationalStatus.ToString(), speed, addresses);
    }

    private async Task<ProfilerInventory> ReadProfiler(CancellationToken cancellationToken)
    {
        try { return ParseProfiler(await _profiler(cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or OperationCanceledException)
        { return new(null, null, [], [], [], ["System", "Memory modules", "Graphics adapters", "Mounted volumes"]); }
    }

    internal static ProfilerInventory ParseProfiler(string output)
    {
        if (output.Length > MaximumCharacters) throw new InvalidDataException("Hardware inventory exceeds its bounds.");
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Unsupported hardware inventory.");
        var unavailable = new List<string>();
        JsonElement[] Devices(string key, string section)
        {
            if (!document.RootElement.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array ||
                value.GetArrayLength() == 0) { unavailable.Add(section); return []; }
            if (value.GetArrayLength() > MaximumDevices) { unavailable.Add(section); return []; }
            var rows = value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray();
            if (rows.Length != value.GetArrayLength()) unavailable.Add(section);
            return rows;
        }
        var hardware = Devices("SPHardwareDataType", "System").FirstOrDefault();
        var memory = new List<MacInventoryMemory>();
        void Memory(JsonElement row)
        {
            if (row.TryGetProperty("_items", out var slots) && slots.ValueKind == JsonValueKind.Array)
            {
                if (slots.GetArrayLength() > MaximumDevices) throw new InvalidDataException("Too many memory slots.");
                foreach (var slot in slots.EnumerateArray()) if (slot.ValueKind == JsonValueKind.Object) Memory(slot);
            }
            else
            {
                if (memory.Count >= MaximumDevices) throw new InvalidDataException("Too many memory devices.");
                memory.Add(new(Text(row, "_name"), Text(row, "dimm_size") ?? Text(row, "SPMemoryDataType"),
                    Text(row, "dimm_type"), Text(row, "dimm_speed"), Text(row, "dimm_manufacturer")));
            }
        }
        foreach (var row in Devices("SPMemoryDataType", "Memory modules")) Memory(row);
        var graphics = Devices("SPDisplaysDataType", "Graphics adapters").Select(row => new MacInventoryGpu(
            Text(row, "sppci_model") ?? Text(row, "_name"), Text(row, "spdisplays_vendor"), Text(row, "sppci_bus"),
            Number(row, "sppci_cores"), Text(row, "spdisplays_vram") ?? Text(row, "spdisplays_vram_shared"))).ToArray();
        var volumes = Devices("SPStorageDataType", "Mounted volumes").Select(row => new MacInventoryVolume(
            Text(row, "_name"), Text(row, "bsd_name"), Text(row, "mount_point"), Text(row, "file_system"),
            Number(row, "size_in_bytes"), Number(row, "free_space_in_bytes"), Text(row, "writable") switch
            { "yes" => true, "no" => false, _ => null as bool? })).ToArray();
        return new(Text(hardware, "machine_name"), Text(hardware, "chip_type") ?? Text(hardware, "cpu_type"),
            memory, graphics, volumes, unavailable);
    }

    private static string? Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object &&
        row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 4096 } text &&
        !text.Any(char.IsControl) && text is not ("unknown" or "Unknown" or "Unknown Manufacturer" or "Empty") ? text : null;
    private static ulong? Number(JsonElement row, string key)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(key, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number) ? number :
            value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static async Task<string> RunProfiler(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = new("/usr/sbin/system_profiler")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (var arg in new[] { "-json", "-detailLevel", "mini", "SPHardwareDataType", "SPMemoryDataType", "SPDisplaysDataType", "SPStorageDataType" })
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["LC_ALL"] = "C";
        process.StartInfo.Environment["LANG"] = "C";
        process.Start();
        try
        {
            var output = ReadBounded(process.StandardOutput, MaximumCharacters, timeout.Token);
            var errors = ReadBounded(process.StandardError, 8192, timeout.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) throw new IOException("Hardware discovery was unavailable.");
            return await output;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken token)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        { if (text.Length + count > limit) throw new InvalidDataException("Hardware discovery exceeds its bounds."); text.Append(buffer, 0, count); }
        return text.ToString();
    }
    private static string? SysctlText(string name)
    {
        nuint length = 0;
        if (Sysctl(name, IntPtr.Zero, ref length, IntPtr.Zero, 0) != 0 || length is < 1 or > 4096) return null;
        var buffer = Marshal.AllocHGlobal((int)length);
        try { return Sysctl(name, buffer, ref length, IntPtr.Zero, 0) == 0 ? Marshal.PtrToStringUTF8(buffer, (int)length)?.TrimEnd('\0') : null; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static ulong? SysctlNumber(string name)
    {
        var buffer = Marshal.AllocHGlobal(8);
        try
        {
            nuint length = 8;
            if (Sysctl(name, buffer, ref length, IntPtr.Zero, 0) != 0) return null;
            return length switch { 4 => (uint)Marshal.ReadInt32(buffer), 8 when Marshal.ReadInt64(buffer) >= 0 => (ulong)Marshal.ReadInt64(buffer), _ => null };
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Sysctl(string name, IntPtr value, ref nuint length, IntPtr newValue, nuint newLength);
}
