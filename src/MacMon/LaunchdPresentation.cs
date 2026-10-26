using System.Globalization;
using System.Text;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record LaunchdRow(LaunchdJob Job, string Label, string State, string Pid, string ExitStatus, string ProcessName)
{
    public override string ToString() => $"{Label}, {State}, PID {Pid}, last {ExitStatus}, observed process {ProcessName}";
}
public sealed class LaunchdInventory
{
    public LaunchdSnapshot? LastSuccess { get; private set; }
    public string? Error { get; private set; }
    public void Apply(LaunchdSnapshot snapshot)
    {
        Error = snapshot.Error;
        if (snapshot.Available) LastSuccess = snapshot;
    }
}

public static class LaunchdPresentation
{
    public static string ExitText(int status) => status < 0 ? $"Signal {-(long)status}" : $"Exit {status}";
    public static ProcessUsage? Match(LaunchdJob job, MonitorSample? live, bool fresh)
    {
        if (!fresh || job.ObservedIdentity is not { } identity) return null;
        return live?.Processes.FirstOrDefault(process => process.Identity == identity);
    }
    public static IReadOnlyList<LaunchdRow> Rows(LaunchdSnapshot? snapshot, MonitorSample? live, bool fresh,
        string? search, int filter, int sort)
    {
        var query = search?.Trim() ?? "";
        var rows = (snapshot?.Jobs ?? []).Where(job => filter switch
        { 1 => job.ReportedPid is not null, 2 => job.ReportedPid is null, _ => true })
            .Select(job => new LaunchdRow(job, job.Id.Label, job.ReportedPid is null ? "Not running" : "Running (reported)",
                job.ReportedPid?.ToString(CultureInfo.InvariantCulture) ?? "—", ExitText(job.LastExitStatus),
                Match(job, live, fresh)?.Name ?? "—"))
            .Where(row => query.Length == 0 || row.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Pid.Contains(query, StringComparison.Ordinal) || row.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase));
        return (sort switch
        {
            1 => rows.OrderByDescending(row => row.Job.ReportedPid is not null).ThenBy(row => row.Label, StringComparer.Ordinal),
            2 => rows.OrderBy(row => row.Job.ReportedPid ?? int.MaxValue).ThenBy(row => row.Label, StringComparer.Ordinal),
            _ => rows.OrderBy(row => row.Label, StringComparer.Ordinal)
        }).ToArray();
    }

    public static string Details(LaunchdJob job, DateTimeOffset observedAt, MonitorSample? live, bool fresh, bool stale)
    {
        var text = new StringBuilder();
        text.AppendLine(job.Id.Label).AppendLine(job.Id.Context.Display)
            .AppendLine($"Observed: {observedAt:yyyy-MM-dd HH:mm:ss zzz}{(stale ? " · STALE inventory" : "")}")
            .AppendLine(job.ReportedPid is null ? "State: Not running (no PID reported)" : $"State: Running at observation · PID {job.ReportedPid}")
            .AppendLine($"Last termination: {ExitText(job.LastExitStatus)}")
            .AppendLine("The last exit status is not current health. Not running does not mean disabled.")
            .AppendLine("Loaded jobs in this context only; disabled state, other domains and child workers are not inventoried.");
        var process = Match(job, live, fresh && !stale);
        if (process is null) text.AppendLine("\nProcess details unavailable: no fresh matching lifetime. No PID-only association is used.");
        else
        {
            text.AppendLine("\nMatched observed process (the job may change after observation):")
                .AppendLine($"{process.Name} · PID {process.ProcessId}")
                .AppendLine($"Started: {process.StartedAtUtc?.ToString("O") ?? "unavailable"}")
                .AppendLine($"Identity: {process.LifetimeDomain} · {process.CreationTicks}")
                .AppendLine($"Executable: {(process.ImagePath.Length == 0 ? "unavailable" : process.ImagePath)}");
        }
        return text.ToString();
    }
}
