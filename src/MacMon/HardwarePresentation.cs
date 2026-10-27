using System.Globalization;
using MonCore.MacOS;

namespace MacMon;

public sealed record HardwareInfoSnapshot(DateTimeOffset ObservedAt, IReadOnlyList<HardwareSection> Sections, bool Partial = false);

public static class HardwarePresentation
{
    public static HardwareInfoSnapshot Create(MacHardwareInventory inventory)
    {
        string Value(string? text) => string.IsNullOrWhiteSpace(text) ? UiText.Get("Unavailable") : text;
        string Count(ulong? count) => count is > 0 ? count.Value.ToString(CultureInfo.CurrentCulture) : UiText.Get("Unavailable");
        string Bytes(ulong? bytes) => bytes is { } value ? TableUnits.Memory(value) : UiText.Get("Unavailable");
        HardwareEntry Entry(string label, string? value) => new(UiText.Get(label), Value(value));
        string Lines(params (string Label, string? Value)[] values) => string.Join("\n", values.Select(v => UiText.Get(v.Label) + ": " + Value(v.Value)));
        HardwareEntry[] Items<T>(IEnumerable<T> values, Func<T, HardwareEntry> entry) => values.Select(entry).ToArray() is { Length: > 0 } items
            ? items : [Entry("Status", UiText.Get("Unavailable"))];
        var sections = new List<HardwareSection>
        {
            new(UiText.Get("System"), [Entry("Computer", Environment.MachineName), Entry("Model", inventory.Model),
                Entry("Model identifier", inventory.ModelIdentifier), Entry("macOS", inventory.OsVersion), Entry("OS build", inventory.OsBuild),
                Entry("Architecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString())]),
            new(UiText.Get("Processor"), [Entry("Chip / processor", inventory.Processor), Entry("Physical CPU cores", Count(inventory.PhysicalCores)),
                Entry("Logical CPU cores", Count(inventory.LogicalCores))]),
            new(UiText.Get("Memory"), [Entry("Installed memory", Bytes(inventory.MemoryBytes)),
                Entry("Scope", UiText.Get("Reported hardware specifications; live usage and pressure remain in the resource tiles."))]),
            new(UiText.Get("Memory details"), Items(inventory.Memory.Select((memory, index) => (memory, index)), item =>
                new(Value(item.memory.Slot ?? UiText.Format("Memory device {0}", item.index + 1)),
                    Lines(("Reported size", item.memory.Size), ("Type", item.memory.Type), ("Reported speed", item.memory.Speed), ("Manufacturer", item.memory.Manufacturer)))), false),
            new(UiText.Get("Graphics adapters"), Items(inventory.Graphics.Select((gpu, index) => (gpu, index)), item =>
                new(Value(item.gpu.Model ?? UiText.Format("Graphics adapter {0}", item.index + 1)),
                    Lines(("Vendor", Vendor(item.gpu.Vendor)), ("Interface", Bus(item.gpu.Bus)), ("GPU cores", Count(item.gpu.Cores)),
                        ("Reported graphics memory", item.gpu.ReportedMemory)))), false),
            new(UiText.Get("Whole disk devices"), Items(inventory.Disks, disk => new(disk.Name, Lines(("Capacity", Bytes(disk.Capacity))))), false),
            new(UiText.Get("Mounted volumes"), Items(inventory.Volumes, volume => new(Value(volume.Name),
                Lines(("Device", volume.Device), ("Mount point", volume.MountPoint), ("File system", volume.FileSystem),
                    ("Capacity", Bytes(volume.Capacity)), ("Available", Bytes(volume.Available)),
                    ("Writable", volume.Writable is { } writable ? UiText.Get(writable ? "Yes" : "No") : null)))), false),
            new(UiText.Get("Network interfaces"), Items(inventory.Networks, adapter => new(adapter.Name,
                Lines(("Description", adapter.Description), ("Type", NetworkType(adapter.Type)), ("Status", UiText.Get(adapter.Status)),
                    ("Reported link speed", adapter.LinkSpeed is > 0 ? ResourceGaugeHistory.NetworkRate(adapter.LinkSpeed.Value / 8d) : null),
                    ("IP addresses", adapter.Addresses.Count > 0 ? string.Join("\n", adapter.Addresses) : null)))), false)
        };
        if (inventory.UnavailableSections.Count > 0)
            sections.Add(new(UiText.Get("Unavailable details"), inventory.UnavailableSections.Select(section => Entry(section, UiText.Get("Unavailable"))).ToArray()));
        sections.Add(new(UiText.Get("Measurement notes"), [
            Entry("Storage", UiText.Get("Whole disk devices can include disk images. Mounted volumes are listed separately; APFS volumes may share container capacity, so do not add their capacities or free space.")),
            Entry("Graphics", UiText.Get("All adapters reported by macOS are listed. Reported graphics memory is a specification, not live allocation or dedicated VRAM capacity on unified-memory hardware.")),
            Entry("Network", UiText.Get("Reported link speed is an adapter property, not measured throughput. IP addresses describe this snapshot."))], false));
        return new(inventory.ObservedAt, sections, inventory.UnavailableSections.Count > 0);
    }

    private static string? Vendor(string? vendor) => vendor switch { "sppci_vendor_Apple" => "Apple", _ => vendor?.StartsWith("sppci_vendor_", StringComparison.Ordinal) == true ? vendor[13..] : vendor };
    private static string? Bus(string? bus) => bus switch { "spdisplays_builtin" => UiText.Get("Built-in"), "spdisplays_pcie" => "PCIe", _ => bus?.StartsWith("spdisplays_", StringComparison.Ordinal) == true ? null : bus };
    private static string NetworkType(string type) => UiText.Get(type switch { "Wireless80211" => "Wi-Fi", "Ethernet" or "GigabitEthernet" or "FastEthernetFx" or "FastEthernetT" => "Ethernet", "Loopback" => "Loopback", "Tunnel" => "Tunnel", _ => type });

    public static string Report(string product, HardwareInfoSnapshot snapshot) => product + "\n" +
        UiText.Format("Observed: {0}", snapshot.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)) +
        (snapshot.Partial ? "\n" + UiText.Get("Some hardware details are unavailable.") : "") + "\n\n" +
        string.Join("\n\n", snapshot.Sections.Select(section => section.Title + "\n" + string.Join("\n", section.Entries.Select(entry => entry.Label + ": " + entry.Value))));
}
