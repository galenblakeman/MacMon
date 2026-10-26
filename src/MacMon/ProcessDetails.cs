using System.Globalization;
using WinMon.Monitoring;

namespace MacMon;

public static class ProcessDetails
{
    public static string Text(PinnedProcess selected, MonitorSample sample, bool grouped, bool historical)
    {
        var p = selected.Usage;
        return $"{p.Name}\nObserved: {sample.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n" +
            $"View: {(historical ? "Historical retained observations" : "Live")} · {selected.State}\n" +
            $"{(grouped ? "Representative PID" : "PID")}: {p.ProcessId}\n" +
            $"Started: {p.StartedAtUtc?.ToLocalTime().ToString("O", CultureInfo.InvariantCulture) ?? "Unavailable"}\n" +
            $"Lifetime: {p.LifetimeDomain} · {p.CreationTicks}\n" +
            $"CPU: {Number(p.ObservedCpuPercent)}% of machine capacity (five-second average)\n" +
            $"Resident{(grouped ? " sum" : "")}: {p.WorkingSetBytes?.ToString(CultureInfo.InvariantCulture) ?? "—"} bytes\n" +
            $"Disk I/O: {Number(p.IoBytesPerSecond)} B/s (five-second average; excludes network)\n" +
            $"Threads: {p.ThreadCount?.ToString(CultureInfo.InvariantCulture) ?? "—"}\n" +
            (grouped ? $"Observed members: {string.Join(", ", p.MemberIds)}\nResident sums may count shared pages repeatedly.\n" : "") +
            $"Executable: {(p.ImagePath.Length == 0 ? "Unavailable" : p.ImagePath)}\n— means unavailable.";
    }
    private static string Number(double? value) => value?.ToString("F1", CultureInfo.InvariantCulture) ?? "—";
}
