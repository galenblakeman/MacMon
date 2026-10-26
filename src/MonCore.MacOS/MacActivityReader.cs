using System.Net.NetworkInformation;

namespace MonCore.MacOS;

public sealed record MacNetworkChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed record MacActivitySnapshot(string NetworkId, string NetworkName, ulong? Received, ulong? Sent,
    string StorageSet, ulong? Read, ulong? Written, IReadOnlyList<MacNetworkChoice> Interfaces)
{
    public IReadOnlyList<MacDiskDevice> Disks { get; init; } = [];
    public static MacActivitySnapshot Empty { get; } = new("", "Unavailable", null, null, "", null, null, []);
}

internal sealed class MacActivityReader
{
    private string? _automaticId;

    public MacActivitySnapshot Read(string? selectedId)
    {
        var storage = DarwinStorageReader.Read();
        var result = MacActivitySnapshot.Empty with { StorageSet = storage.Key, Read = storage.Read, Written = storage.Written, Disks = storage.Devices };
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
            var choices = interfaces.Select(adapter => new MacNetworkChoice(adapter.Id, $"{adapter.Name} · {adapter.Description}"))
                .OrderBy(adapter => adapter.Name, StringComparer.Ordinal).ToArray();
            var active = interfaces.Where(adapter => adapter.OperationalStatus == OperationalStatus.Up).ToArray();
            var adapter = selectedId is not null ? interfaces.FirstOrDefault(adapter => adapter.Id == selectedId)
                : active.FirstOrDefault(adapter => adapter.Id == _automaticId) ??
                  active.FirstOrDefault(adapter => adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211);
            if (adapter is null || adapter.OperationalStatus != OperationalStatus.Up) return result with { Interfaces = choices };
            if (selectedId is null) _automaticId = adapter.Id;
            var counters = adapter.GetIPStatistics();
            return result with
            {
                NetworkId = adapter.Id, NetworkName = adapter.Name,
                Received = counters.BytesReceived >= 0 ? (ulong)counters.BytesReceived : null,
                Sent = counters.BytesSent >= 0 ? (ulong)counters.BytesSent : null, Interfaces = choices
            };
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        { return result; }
    }
}
