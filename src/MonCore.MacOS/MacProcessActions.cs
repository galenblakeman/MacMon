using System.Runtime.InteropServices;
using WinMon.Monitoring;

namespace MonCore.MacOS;

public sealed class MacProcessActionTarget
{
    internal MacProcessActionTarget(ProcessUsage process, AuditToken token) { Process = process; Token = token; }
    public ProcessUsage Process { get; }
    internal AuditToken Token { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct AuditToken
{
    public uint Value0, Value1, Value2, Value3, Value4, Value5, Value6, Value7;
}

internal sealed record MacActionSnapshot(NativeProcessInfo Info, AuditToken Token, string ImagePath);
internal interface IMacSignalApi
{
    uint UserId { get; }
    int SelfPid { get; }
    MacActionSnapshot? ReadTarget(int pid);
    int Signal(AuditToken token, int signal);
}

/// <summary>Prepare targets before confirmation, then deliver SIGTERM to the captured audit-token generation.</summary>
public sealed class MacProcessActions
{
    private readonly IMacSignalApi _api;
    public MacProcessActions() : this(new DarwinSignalApi()) { }
    internal MacProcessActions(IMacSignalApi api) => _api = api;

    public MacProcessActionTarget? Prepare(ProcessUsage process)
    {
        if (process.ProcessId <= 1 || process.ProcessId == _api.SelfPid ||
            process.CreationTicks <= 0 || process.LifetimeDomain != ProcessLifetimeDomain.UnixMicroseconds) return null;
        var snapshot = _api.ReadTarget(process.ProcessId);
        return Matches(process, snapshot) ? new(process, snapshot!.Token) : null;
    }

    public string? Terminate(MacProcessActionTarget target)
    {
        if (target.Process.ProcessId <= 1 || target.Process.ProcessId == _api.SelfPid ||
            !Matches(target.Process, _api.ReadTarget(target.Process.ProcessId)))
            return "The selected process changed, exited, or is no longer accessible.";
        // The kernel validates the original token's PID version at delivery, closing the lookup-to-signal race.
        var error = _api.Signal(target.Token, 15);
        return error == 0 ? null : $"macOS rejected the termination request (error {error}).";
    }

    private bool Matches(ProcessUsage selected, MacActionSnapshot? snapshot) => snapshot is { Info: { } current } &&
        (selected.ImagePath.Length == 0 || selected.ImagePath == snapshot.ImagePath) &&
        current.ProcessId == selected.ProcessId && current.EffectiveUserId is > 0 &&
        current.EffectiveUserId == _api.UserId && current.StartMicroseconds < 1_000_000 &&
        (UInt128)current.StartSeconds * 1_000_000 + current.StartMicroseconds == (UInt128)selected.CreationTicks;
}

internal sealed partial class DarwinSignalApi : IMacSignalApi
{
    private const string System = "/usr/lib/libSystem.B.dylib";
    public uint UserId => GetEffectiveUserId();
    public int SelfPid => Environment.ProcessId;

    public MacActionSnapshot? ReadTarget(int pid)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var self = TaskSelf();
        if (TaskNameForPid(self, pid, out var task) != 0) return null;
        try
        {
            uint count = 8;
            if (TaskInfo(task, 15, out var token, ref count) != 0 || count != 8 || TokenPid(token) != pid) return null;
            // BSD identity is read after obtaining the token: a reused PID cannot acquire an old target's token.
            var reader = new DarwinProcessApi();
            var path = reader.ReadPath(pid);
            var info = reader.ReadIdentity(pid);
            return info is null ? null : new(info, token, path);
        }
        finally { MachPortDeallocate(self, task); }
    }

    public int Signal(AuditToken token, int signal) => SignalWithAuditToken(ref token, signal);

    [LibraryImport(System, EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
    [LibraryImport(System, EntryPoint = "task_self_trap")]
    private static partial uint TaskSelf();
    [LibraryImport(System, EntryPoint = "task_name_for_pid")]
    private static partial int TaskNameForPid(uint self, int pid, out uint task);
    [LibraryImport(System, EntryPoint = "task_info")]
    private static partial int TaskInfo(uint task, int flavor, out AuditToken token, ref uint count);
    [LibraryImport(System, EntryPoint = "mach_port_deallocate")]
    private static partial int MachPortDeallocate(uint self, uint port);
    [LibraryImport("/usr/lib/libbsm.dylib", EntryPoint = "audit_token_to_pid")]
    private static partial int TokenPid(AuditToken token);
    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_signal_with_audittoken")]
    private static partial int SignalWithAuditToken(ref AuditToken token, int signal);
}
