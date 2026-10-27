using System.Runtime.InteropServices;

namespace MacMon;

// Values match SMAppService.Status; unknown native values remain unavailable in the UI.
public enum LoginItemStatus { Unavailable = -1, NotRegistered = 0, Enabled = 1, RequiresApproval = 2, NotFound = 3 }

public interface ILoginItemService
{
    LoginItemStatus Status { get; }
    string? SetEnabled(bool enabled);
    void OpenSettings();
}

public sealed class NativeLoginItemService : ILoginItemService
{
    public LoginItemStatus Status => (LoginItemStatus)MacDesktopIntegration.LoginStatus;
    public string? SetEnabled(bool enabled) => MacDesktopIntegration.SetLogin(enabled);
    public void OpenSettings() => MacDesktopIntegration.OpenLoginSettings();
}

public sealed record ShortcutChoice(string Id, string Label, uint Key = uint.MaxValue, uint Modifiers = 0)
{
    public override string ToString() => Label;
}

/// <summary>Optional signed-app services. Merely opening Settings never changes login items or asks permission.</summary>
public static class MacDesktopIntegration
{
    public static IReadOnlyList<ShortcutChoice> Shortcuts { get; } = [new("Off", "Off"),
        new("ControlAltM", "⌃⌥M", 46, 4096 | 2048), new("ControlAltShiftM", "⌃⌥⇧M", 46, 4096 | 2048 | 512),
        new("ControlAltSpace", "⌃⌥Space", 49, 4096 | 2048)];
    public static string NormalizeShortcut(string? value) => Shortcuts.Any(c => c.Id == value) ? value! : "Off";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void HotkeyCallback();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PermissionCallback(int status);
    private static readonly HotkeyCallback Callback = () => Activated?.Invoke();
    public static event Action? Activated;
    public static int LoginStatus
    {
        get { try { return OperatingSystem.IsMacOS() ? LoginStatusNative() : -1; } catch (Exception ex) when (Unavailable(ex)) { return -1; } }
    }
    private static bool Unavailable(Exception ex) => ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
    public static string? SetLogin(bool enabled)
    {
        try
        {
            var status = SetLoginNative(enabled ? 1 : 0);
            return status is 0 or 1 or 2 ? null : "macOS could not update Open at login. Use Login Items in System Settings.";
        }
        catch (Exception ex) when (Unavailable(ex)) { return "Open at login requires the packaged MacMon app."; }
    }
    public static string? SetShortcut(string id)
    {
        var choice = Shortcuts.First(c => c.Id == NormalizeShortcut(id));
        try
        {
            var status = SetHotkey(choice.Key, choice.Modifiers, Marshal.GetFunctionPointerForDelegate(Callback));
            return status == 0 || status == -1 && choice.Id == "Off" ? null : status == -1
                ? "The global shortcut requires the packaged MacMon app." : "That shortcut is unavailable or already used by another app. Choose another combination.";
        }
        catch (Exception ex) when (Unavailable(ex)) { return choice.Id == "Off" ? null : "The global shortcut requires the packaged MacMon app."; }
    }
    public static async Task<bool> RequestNotifications()
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PermissionCallback callback = status => result.TrySetResult(status == 1);
        try
        {
            RequestNotificationsNative(Marshal.GetFunctionPointerForDelegate(callback));
            var allowed = await result.Task; GC.KeepAlive(callback); return allowed;
        }
        catch (Exception ex) when (Unavailable(ex)) { return false; }
    }
    public static void Notify(string title, string message)
    { try { NotifyCapture(title, message); } catch (Exception ex) when (Unavailable(ex)) { } }
    public static void Activate() { try { ActivateNative(); } catch (Exception ex) when (Unavailable(ex)) { } }
    public static void OpenLoginSettings() { try { OpenLoginSettingsNative(); } catch (Exception ex) when (Unavailable(ex)) { } }
    public static void Dispose() { try { DisposeNative(); } catch (Exception ex) when (Unavailable(ex)) { } }
    [DllImport("MacMonIntegration", EntryPoint = "macmon_login_status")] private static extern int LoginStatusNative();
    [DllImport("MacMonIntegration", EntryPoint = "macmon_set_login")] private static extern int SetLoginNative(int enabled);
    [DllImport("MacMonIntegration", EntryPoint = "macmon_set_hotkey")] private static extern int SetHotkey(uint key, uint modifiers, IntPtr callback);
    [DllImport("MacMonIntegration", EntryPoint = "macmon_request_notifications")] private static extern void RequestNotificationsNative(IntPtr callback);
    [DllImport("MacMonIntegration", EntryPoint = "macmon_notify_capture", CharSet = CharSet.Ansi)] private static extern void NotifyCapture([MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [DllImport("MacMonIntegration", EntryPoint = "macmon_activate")] private static extern void ActivateNative();
    [DllImport("MacMonIntegration", EntryPoint = "macmon_open_login_settings")] private static extern void OpenLoginSettingsNative();
    [DllImport("MacMonIntegration", EntryPoint = "macmon_desktop_dispose")] private static extern void DisposeNative();
}
