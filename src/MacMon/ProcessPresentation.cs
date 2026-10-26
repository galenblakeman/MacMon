using System.Diagnostics;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record ProcessTrail(IReadOnlyList<double?> Values);

/// <summary>Builds visible rows from one ranking and retained observations, with explicit missing trail values.</summary>
public static class ProcessPresentation
{
    private static ProcessIdentityPolicy Policy => MacFrameSource.IdentityPolicy;
    private readonly record struct Key(string? Executable, ProcessLifetimeIdentity Lifetime);
    private static Key Identity(ProcessUsage process, bool grouped) => grouped
        ? new(Policy.ExecutableKey(process), default) : new(null, process.Identity);

    public static ProcessRow[] Rows(IReadOnlyList<MonitorSample> history, MonitorSample latest,
        bool grouped, ProcessSort sort, string search, ProcessPins pins, bool pinnedOnly, bool historical = false)
    {
        var identities = new Dictionary<ProcessLifetimeIdentity, (string Path, Key Key)>();
        Key KeyOf(ProcessUsage process)
        {
            if (!grouped) return Identity(process, false);
            if (identities.TryGetValue(process.Identity, out var cached) && cached.Path == process.ImagePath)
                return cached.Key;
            var key = Identity(process, true);
            identities[process.Identity] = (process.ImagePath, key);
            return key;
        }
        var ranking = ProcessRanking.Rank(history, grouped: grouped, sort: sort,
            includeInactive: true, identityPolicy: Policy);
        var current = latest.Processes.Select(process => KeyOf(process)).ToHashSet();
        var live = ranking.Processes.Where(process => current.Contains(KeyOf(process)));
        // MonCore's grouped memory sort uses Windows private bytes. Mac rows explicitly show resident sums.
        if (grouped && sort == ProcessSort.Memory)
            live = live.OrderByDescending(process => process.WorkingSetBytes).ThenBy(process => process.ProcessId);
        var pinned = pins.Resolve(latest, ranking.Processes, grouped, historical);
        var rows = pinned.Select(pin => new ProcessRow(pin.Usage, true, pin.State, grouped))
            .Concat(pinnedOnly ? [] : live.Where(process => !pins.Contains(process, grouped))
                .Select(process => new ProcessRow(process, false, PinnedProcessState.Present, grouped)))
            .Where(row => string.IsNullOrWhiteSpace(search) || ProcessSearch.Filter([row.Process], search).Any()).ToArray();
        var end = latest.MonotonicTimestamp;
        var timeline = new List<MonitorSample?>();
        long previousTime = 0;
        foreach (var sample in history)
        {
            if ((end - sample.MonotonicTimestamp) / (double)Stopwatch.Frequency > 30) continue;
            if (previousTime != 0 && (sample.MonotonicTimestamp - previousTime) / (double)Stopwatch.Frequency > 2.5)
                timeline.Add(null);
            previousTime = sample.MonotonicTimestamp;
            timeline.Add(sample);
        }
        var indexes = new Dictionary<Key, int>(rows.Length);
        var trails = new double?[rows.Length][];
        for (var i = 0; i < rows.Length; i++)
        {
            indexes.Add(KeyOf(rows[i].Process), i);
            trails[i] = new double?[timeline.Count];
        }
        var observed = new bool[rows.Length];
        for (var instant = 0; instant < timeline.Count; instant++)
        {
            if (timeline[instant] is not { } sample) continue;
            Array.Clear(observed);
            foreach (var process in sample.Processes)
            {
                if (!indexes.TryGetValue(KeyOf(process), out var row)) continue;
                double? value = sort switch
                {
                    ProcessSort.Memory => process.WorkingSetBytes,
                    ProcessSort.Threads => process.ThreadCount,
                    _ => sample.IsGap ? null : process.ObservedCpuPercent
                };
                trails[row][instant] = observed[row] ? trails[row][instant] + value : value;
                observed[row] = true;
            }
        }
        for (var i = 0; i < rows.Length; i++) rows[i] = rows[i] with { Trail = new(trails[i]) };
        return rows;
    }
}

public sealed record ProcessRow(ProcessUsage Process, bool Pinned = false,
    PinnedProcessState State = PinnedProcessState.Present, bool Grouped = false)
{
    public ProcessTrail Trail { get; init; } = new([]);
    public string Pin => Pinned ? "★" : "";
    public string Name => State == PinnedProcessState.Present ? Process.Name : $"{Process.Name} · {State}";
    public string Pid => Grouped ? $"{Process.MemberCount} procs" : Process.ProcessId.ToString();
    public string Cpu => State == PinnedProcessState.Present && Process.ObservedCpuPercent is { } value ? $"{value:F1}%" : "—";
    public string Resident => State == PinnedProcessState.Present && Process.WorkingSetBytes is { } bytes ? $"{bytes / 1048576d:F1} MiB" : "—";
    public string Threads => State == PinnedProcessState.Present ? Process.ThreadCount?.ToString() ?? "—" : "—";
    public string Path => (Process.ImagePath.Length > 0 ? Process.ImagePath : "Executable path unavailable") +
        (Grouped ? "\nResident sum may count shared pages more than once." : "") +
        (State != PinnedProcessState.Present ? $"\n{State}" : "");
    public override string ToString() => $"{Name}, {Pid}, CPU {Cpu}, resident memory {Resident}, {Threads} threads, {State}";
}
