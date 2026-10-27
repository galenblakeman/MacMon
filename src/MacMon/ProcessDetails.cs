using System.Globalization;
using WinMon.Monitoring;

namespace MacMon;

public static class ProcessDetails
{
    public static string State(PinnedProcessState state) => UiText.Get(state.ToString());
    public static string Text(PinnedProcess selected, MonitorSample sample, bool grouped, bool historical)
    {
        var p = selected.Usage;
        var lines = new List<string> { p.Name,
            UiText.Interpolate($"Observed: {sample.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}"),
            UiText.Format("View: {0} · {1}", UiText.Get(historical ? "Historical retained observations" : "Live"), State(selected.State)),
            UiText.Format("{0}: {1}", UiText.Get(grouped ? "Representative PID" : "PID"), p.ProcessId),
            UiText.Format("Started: {0}", p.StartedAtUtc?.ToLocalTime().ToString("O", CultureInfo.InvariantCulture) ?? UiText.Get("Unavailable")),
            UiText.Interpolate($"Lifetime: {p.LifetimeDomain} · {p.CreationTicks}"),
            UiText.Format("CPU: {0}% of machine capacity (five-second average)", Number(p.ObservedCpuPercent)),
            UiText.Format("{0}: {1} bytes", UiText.Get(grouped ? "Resident sum" : "Resident"), p.WorkingSetBytes?.ToString(CultureInfo.CurrentCulture) ?? "—"),
            UiText.Format("Disk I/O: {0} (five-second average; excludes network)", TableUnits.IoRate(p.IoBytesPerSecond)),
            UiText.Format("Threads: {0}", p.ThreadCount?.ToString(CultureInfo.CurrentCulture) ?? "—") };
        if (grouped) { lines.Add(UiText.Format("Observed members: {0}", string.Join(", ", p.MemberIds))); lines.Add(UiText.Get("Resident sums may count shared pages repeatedly.")); }
        lines.Add(UiText.Format("Executable: {0}", p.ImagePath.Length == 0 ? UiText.Get("Unavailable") : p.ImagePath));
        lines.Add(UiText.Get("— means unavailable."));
        return string.Join("\n", lines);
    }
    private static string Number(double? value) => value?.ToString("F1", CultureInfo.CurrentCulture) ?? "—";
}
