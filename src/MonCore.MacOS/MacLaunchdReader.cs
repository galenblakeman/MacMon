using System.Diagnostics;
using System.Globalization;
using WinMon.Monitoring;

namespace MonCore.MacOS;

// ContextId is local to this reader. Manager names are not canonical launchd domain IDs.
public sealed record LaunchdContext(Guid ContextId, uint UserId, string Manager)
{
    public string Display => $"Current bootstrap context · UID {UserId} · {Manager}";
}
public sealed record LaunchdJobId(LaunchdContext Context, string Label);
public sealed record LaunchdJob(LaunchdJobId Id, int? ReportedPid, int LastExitStatus,
    ProcessLifetimeIdentity? ObservedIdentity = null);
public sealed record LaunchdSnapshot(DateTimeOffset ObservedAt, LaunchdContext? Context,
    IReadOnlyList<LaunchdJob> Jobs, string? Error = null)
{
    public bool Available => Error is null && Context is not null;
}

internal sealed record LaunchctlResult(int ExitCode, string Output);
internal interface ILaunchctlRunner
{
    Task<LaunchctlResult> Run(string command, CancellationToken cancellationToken);
}

/// <summary>Read-only jobs loaded in the caller's bootstrap context. No mutation or plist discovery.</summary>
public sealed class MacLaunchdReader
{
    private readonly ILaunchctlRunner _runner;
    private readonly IMacProcessApi _processes;
    private readonly Guid _contextId = Guid.NewGuid();
    public MacLaunchdReader() : this(new LaunchctlRunner(), new DarwinProcessApi()) { }
    internal MacLaunchdReader(ILaunchctlRunner runner, IMacProcessApi processes)
    { _runner = runner; _processes = processes; }

    public async Task<LaunchdSnapshot> Read(CancellationToken cancellationToken = default)
    {
        try
        {
            var uidText = await ReadCommand("manageruid", cancellationToken);
            var manager = (await ReadCommand("managername", cancellationToken)).Trim();
            if (!uint.TryParse(uidText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var uid) ||
                uid == 0 || manager.Length is < 1 or > 128 || manager.Any(char.IsControl))
                throw new InvalidDataException("The current user bootstrap context could not be identified.");
            var context = new LaunchdContext(_contextId, uid, manager);
            var before = new Dictionary<int, ProcessLifetimeIdentity>();
            foreach (var pid in _processes.EnumeratePids() ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pid > 0 && Identity(_processes.ReadIdentity(pid), uid, pid) is { } identity) before[pid] = identity;
            }
            var jobs = Parse(await ReadCommand("list", cancellationToken), context);
            var verified = jobs.Select(job => job.ReportedPid is { } pid && before.TryGetValue(pid, out var identity) &&
                Identity(_processes.ReadIdentity(pid), uid, pid) == identity ? job with { ObservedIdentity = identity } : job).ToArray();
            return new(DateTimeOffset.Now, context, verified);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or TimeoutException or PlatformNotSupportedException)
        { return new(DateTimeOffset.Now, null, [], ex.Message); }
    }

    private async Task<string> ReadCommand(string command, CancellationToken cancellationToken)
    {
        var result = await _runner.Run(command, cancellationToken);
        if (result.ExitCode != 0) throw new IOException($"launchctl {command} was unavailable (exit {result.ExitCode}).");
        return result.Output;
    }

    private static ProcessLifetimeIdentity? Identity(NativeProcessInfo? info, uint uid, int pid) =>
        info is { ProcessId: > 0, StartSeconds: > 0, StartMicroseconds: < 1_000_000 } && info.ProcessId == pid && info.EffectiveUserId == uid &&
        info.StartSeconds <= ((ulong)long.MaxValue - info.StartMicroseconds) / 1_000_000
            ? new(info.ProcessId, (long)(info.StartSeconds * 1_000_000 + info.StartMicroseconds), ProcessLifetimeDomain.UnixMicroseconds) : null;

    public static IReadOnlyList<LaunchdJob> Parse(string output, LaunchdContext context)
    {
        if (output.Length > LaunchctlRunner.MaxCharacters) throw new InvalidDataException("launchctl output exceeded its bounds.");
        using var reader = new StringReader(output);
        if (reader.ReadLine() != "PID\tStatus\tLabel") throw new InvalidDataException("Unsupported launchctl list header.");
        var jobs = new List<LaunchdJob>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            var columns = line.Split('\t');
            if (columns.Length != 3 || columns[2].Length is < 1 or > 4096 || columns[2].Any(char.IsControl) ||
                !int.TryParse(columns[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var status) ||
                !labels.Add(columns[2]) || jobs.Count >= 20_000)
                throw new InvalidDataException("Unsupported or incomplete launchctl job rows.");
            int? pid = null;
            if (columns[0] != "-")
            {
                if (!int.TryParse(columns[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    throw new InvalidDataException("Invalid launchctl job PID.");
                pid = parsed;
            }
            jobs.Add(new(new(context, columns[2]), pid, status));
        }
        return jobs;
    }
}

internal sealed class LaunchctlRunner : ILaunchctlRunner
{
    internal const int MaxCharacters = 2_097_152;
    public async Task<LaunchctlResult> Run(string command, CancellationToken cancellationToken)
    {
        if (command is not ("list" or "manageruid" or "managername")) throw new ArgumentException("Unsupported read command.");
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("launchd monitoring requires macOS.");
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var process = new Process { StartInfo = new("/bin/launchctl")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add(command);
        process.StartInfo.Environment["LC_ALL"] = "C";
        process.StartInfo.Environment["LANG"] = "C";
        process.Start();
        try
        {
            var output = ReadBounded(process.StandardOutput, command == "list" ? MaxCharacters : 256, timeout.Token);
            var error = ReadBounded(process.StandardError, 8192, timeout.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            return new(process.ExitCode, await output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("launchctl discovery timed out."); }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, int maximum, CancellationToken cancellationToken)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (text.Length + count > maximum) throw new InvalidDataException("launchctl output exceeded its bounds.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
