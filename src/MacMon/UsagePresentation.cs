using System.Globalization;
using WinMon.Monitoring;

namespace MacMon;

public enum UsageSort { Cpu, DiskIo, LastActive }

public sealed record UsageRow(AppUsage App, long MachineBusyTicks)
{
    public string Name => App.Name;
    public string Cpu => $"{TimeSpan.FromTicks(App.CpuTicks).TotalSeconds:F1} s";
    public string Share => MachineBusyTicks > 0 ? $"{100d * App.CpuTicks / MachineBusyTicks:F1}%" : "—";
    public string DiskIo => $"{App.IoBytes / 1048576:F1} MiB";
    public string Processes => $"{App.Running} / {App.Lifetimes}";
    public string LastActive => App.LastActive?.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
    public string Path => App.ImagePath;
    public override string ToString() => $"{Name}, CPU {Cpu}, disk I/O {DiskIo}, running/seen {Processes}";
}

public static class UsagePresentation
{
    public static UsageRow[] Rows(UsageLedger ledger, string search, UsageSort sort)
    {
        var apps = ledger.Apps.Where(app => app.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ||
            app.ImagePath.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        apps = sort switch
        {
            UsageSort.DiskIo => apps.OrderByDescending(app => app.IoBytes).ThenBy(app => app.Key, StringComparer.Ordinal),
            UsageSort.LastActive => apps.OrderByDescending(app => app.LastActive?.Timestamp).ThenBy(app => app.Key, StringComparer.Ordinal),
            _ => apps.OrderByDescending(app => app.CpuTicks).ThenBy(app => app.Key, StringComparer.Ordinal)
        };
        return apps.Select(app => new UsageRow(app, ledger.MachineBusyTicks)).ToArray();
    }
}
