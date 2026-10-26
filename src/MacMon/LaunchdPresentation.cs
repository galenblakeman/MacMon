using System.Globalization;
using System.Text;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record LaunchdRow(LaunchdJob Job, string Label, string State, string Pid, string ExitStatus, string ProcessName)
{
    public ProcessUsage? Process { get; init; }
    public ProcessTrail Trail { get; init; } = new([]);
    public string Name => Label.StartsWith("application.", StringComparison.Ordinal) && ProcessName != "—" ? ProcessName : Label;
    public string Cpu => Process?.ObservedCpuPercent is { } value ? $"{value:F1}%" : "—";
    public string Resident => Process?.WorkingSetBytes is { } value ? $"{value / 1048576d:F1} MiB" : "—";
    public string Io => Process?.IoBytesPerSecond is { } value ? $"{value / 1048576:F2}" : "—";
    public string ProcessDescription => $"{(Name == Label ? ProcessName : Label)} · PID {Pid}";
    public string Description => $"{Label}\n{State} · PID {Pid} · last {ExitStatus}\nResource readings describe only a fresh, verified direct process; child workers are excluded.";
    public override string ToString() => $"{Label}, {State}, PID {Pid}, last {ExitStatus}, observed process {ProcessName}";
}
public sealed class LaunchdInventory(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long _receivedAt;
    public TimeSpan Age => LastSuccess is null ? TimeSpan.MaxValue : _clock.GetElapsedTime(_receivedAt);
    public bool IsStale => LastSuccess is not null && (Error is not null || Age.TotalSeconds >= 15);
    public LaunchdSnapshot? LastSuccess { get; private set; }
    public string? Error { get; private set; }
    public void Apply(LaunchdSnapshot snapshot)
    {
        Error = snapshot.Error;
        if (snapshot.Available) { LastSuccess = snapshot; _receivedAt = _clock.GetTimestamp(); }
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
        string? search, int filter, int sort, int limit = 0, IReadOnlyList<MonitorSample>? history = null)
    {
        var query = search?.Trim() ?? "";
        var current = fresh && live is not null ? live.Processes.ToDictionary(p => p.Identity) : [];
        var rows = (snapshot?.Jobs ?? []).Where(job => filter switch
        { 1 => job.ReportedPid is not null, 2 => job.ReportedPid is null, _ => true })
            .Select(job => new LaunchdRow(job, job.Id.Label, job.ReportedPid is null ? "Not running" : "Running (reported)",
                job.ReportedPid?.ToString(CultureInfo.InvariantCulture) ?? "—", ExitText(job.LastExitStatus),
                job.ObservedIdentity is { } identity && current.TryGetValue(identity, out var process) ? process.Name : "—")
            { Process = job.ObservedIdentity is { } key && current.TryGetValue(key, out var matched) ? matched : null })
            .Where(row => query.Length == 0 || row.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Pid.Contains(query, StringComparison.Ordinal) || row.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase));
        var sorted = (sort switch
        {
            1 => rows.OrderByDescending(row => row.Job.ReportedPid is not null).ThenBy(row => row.Label, StringComparer.Ordinal),
            2 => rows.OrderBy(row => row.Job.ReportedPid ?? int.MaxValue).ThenBy(row => row.Label, StringComparer.Ordinal),
            3 => rows.OrderByDescending(row => row.Process?.ObservedCpuPercent).ThenBy(row => row.Label, StringComparer.Ordinal),
            4 => rows.OrderByDescending(row => row.Process?.WorkingSetBytes).ThenBy(row => row.Label, StringComparer.Ordinal),
            5 => rows.OrderByDescending(row => row.Process?.IoBytesPerSecond).ThenBy(row => row.Label, StringComparer.Ordinal),
            _ => rows.OrderBy(row => row.Label, StringComparer.Ordinal)
        });
        var maximum = TablePresentation.NormalizeLimit(limit);
        var shown = (maximum == 0 ? sorted.AsEnumerable() : sorted.Take(maximum)).ToArray();
        if (history is null || history.Count == 0) return shown;
        // Index each observation once; identity includes the PID generation and lifetime domain.
        var instants = new List<(MonitorSample Sample, Dictionary<ProcessLifetimeIdentity, ProcessUsage> Processes)>();
        long previous = 0;
        foreach (var sample in history)
        {
            if (previous != 0 && (sample.MonotonicTimestamp - previous) / (double)System.Diagnostics.Stopwatch.Frequency > 2.5)
                instants.Add((sample with { IsGap = true }, []));
            instants.Add((sample, sample.Processes.ToDictionary(p => p.Identity)));
            previous = sample.MonotonicTimestamp;
        }
        var trailed = shown.Select(row => row with { Trail = new(row.Process is not { } process ? [] :
            instants.Select(instant => (instant.Sample.IsGap && sort != 4) || !instant.Processes.TryGetValue(process.Identity, out var observed)
                ? (double?)null : sort switch { 4 => observed.WorkingSetBytes, 5 => observed.IoBytesPerSecond, _ => observed.ObservedCpuPercent }).ToArray()) }).ToArray();
        var maximumTrail = sort is 4 or 5 ? Math.Max(1, trailed.SelectMany(row => row.Trail.Values).Max() ?? 1) : 100;
        return trailed.Select(row => row with { Trail = row.Trail with { Maximum = maximumTrail } }).ToArray();
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
