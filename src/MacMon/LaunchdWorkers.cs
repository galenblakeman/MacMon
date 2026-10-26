using System.Text;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record JobWorkerNode(ProcessUsage Process, IReadOnlyList<JobWorkerNode> Children)
{
    public string Name => Process.Name;
    public string Pid => $"PID {Process.ProcessId}";
    public string Cpu => Process.ObservedCpuPercent is { } value ? $"{value:F1}%" : "—";
    public string Memory => TableUnits.Memory(Process.WorkingSetBytes);
    public string Io => TableUnits.IoRate(Process.IoBytesPerSecond);
    public string Identity => $"{Process.ImagePath}\n{Process.LifetimeDomain} · {Process.CreationTicks}";
}
public sealed record JobWorkerTree(JobWorkerNode? Root, bool Truncated)
{
    public string Text()
    {
        var text = new StringBuilder("Observed process tree · read only\n");
        void Append(JobWorkerNode node, int depth)
        {
            text.Append(' ', depth * 2).AppendLine($"{node.Name} · {node.Pid} · CPU {node.Cpu} · Memory {node.Memory} · I/O {node.Io}");
            foreach (var child in node.Children) Append(child, depth + 1);
        }
        if (Root is not null) Append(Root, 0);
        else text.AppendLine("No verified process lifetime is available.");
        text.AppendLine("Only observed parent-child lifetimes are shown. Reparented workers and other loaded jobs are excluded; values are not added to the job's direct-process readings.");
        if (Truncated) text.AppendLine("Tree truncated at the node/depth limit.");
        return text.ToString();
    }
}
public static class LaunchdWorkers
{
    public const int MaximumNodes = 512;
    public const int MaximumDepth = 16;
    public static JobWorkerTree Build(LaunchdJob job, LaunchdSnapshot snapshot, MonitorSample? sample, bool fresh)
    {
        var root = LaunchdPresentation.Match(job, sample, fresh);
        if (root is null || sample is null) return new(null, false);
        var otherJobs = snapshot.Jobs.Where(j => j.Id != job.Id && j.ObservedIdentity is not null).Select(j => j.ObservedIdentity!.Value).ToHashSet();
        var children = sample.Processes.Where(p => p.ParentProcessId > 1 && !otherJobs.Contains(p.Identity))
            .GroupBy(p => p.ParentProcessId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.ProcessId).ToArray());
        var visited = new HashSet<ProcessLifetimeIdentity>(); var count = 0; var truncated = false;
        JobWorkerNode Node(ProcessUsage parent, int depth)
        {
            visited.Add(parent.Identity); count++;
            var nodes = new List<JobWorkerNode>();
            foreach (var child in children.GetValueOrDefault(parent.ProcessId) ?? [])
            {
                // A reused parent PID starts after an older child. Require same-domain,
                // ordered native lifetime tokens, never a PID-only or executable-name join.
                if (visited.Contains(child.Identity) || parent.LifetimeDomain != ProcessLifetimeDomain.UnixMicroseconds ||
                    child.LifetimeDomain != parent.LifetimeDomain || parent.CreationTicks <= 0 || child.CreationTicks < parent.CreationTicks) continue;
                if (depth >= MaximumDepth || count >= MaximumNodes) { truncated = true; continue; }
                nodes.Add(Node(child, depth + 1));
            }
            return new(parent, nodes);
        }
        return new(Node(root, 0), truncated);
    }
}
