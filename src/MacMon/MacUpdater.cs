using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MacMon;

/// <summary>Sparkle owns update scheduling, signature verification, installation and its preferences.</summary>
public static class MacUpdater
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseCallback();
    private static CloseCallback? _close;
    public static bool RestartRequested { get; private set; }
    public static bool Available { get; private set; }
    public static void Start(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        _close = () => Dispatcher.UIThread.Post(() => { RestartRequested = true; window.Close(); });
        try { Available = StartNative(_close) == 0; }
        catch (Exception ex) when (Unavailable(ex)) { Available = false; }
    }
    public static bool AutomaticChecks => Available && AutomaticNative() == 1;
    public static void SetAutomaticChecks(bool enabled) { if (Available) SetAutomaticNative(enabled ? 1 : 0); }
    public static void Check() { if (Available) CheckNative(); }
    public static void CompleteShutdown() { if (Available) CompleteShutdownNative(); }
    private static bool Unavailable(Exception ex) => ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
    [DllImport("MacMonIntegration", EntryPoint = "macmon_updates_start")] private static extern int StartNative(CloseCallback close);
    [DllImport("MacMonIntegration", EntryPoint = "macmon_updates_check")] private static extern void CheckNative();
    [DllImport("MacMonIntegration", EntryPoint = "macmon_updates_automatic")] private static extern int AutomaticNative();
    [DllImport("MacMonIntegration", EntryPoint = "macmon_updates_set_automatic")] private static extern void SetAutomaticNative(int enabled);
    [DllImport("MacMonIntegration", EntryPoint = "macmon_updates_complete_shutdown")] private static extern void CompleteShutdownNative();
}
