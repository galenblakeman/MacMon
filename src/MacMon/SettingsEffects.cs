namespace MacMon;

public interface IDesktopSettingsServices
{
    int LoginStatus { get; }
    string? SetLogin(bool enabled);
    string? SetShortcut(string id);
    Task<bool> RequestNotifications();
}

public sealed class NativeDesktopSettingsServices : IDesktopSettingsServices
{
    public int LoginStatus => MacDesktopIntegration.LoginStatus;
    public string? SetLogin(bool enabled) => MacDesktopIntegration.SetLogin(enabled);
    public string? SetShortcut(string id) => MacDesktopIntegration.SetShortcut(id);
    public Task<bool> RequestNotifications() => MacDesktopIntegration.RequestNotifications();
}

/// <summary>Apply native draft changes only on Save; recover native state if persistence fails.</summary>
public static class SettingsEffects
{
    public static async Task<string?> Save(MacPreferences current, MacPreferences draft, bool? login,
        IDesktopSettingsServices services, Func<Task> persist)
    {
        if (draft.Captures.Notify && !current.Captures.Notify && !await services.RequestNotifications())
            return "Notifications were not allowed. Enable them in macOS Settings, or turn off capture notifications.";
        var previousLogin = services.LoginStatus;
        var changedLogin = login is not null && previousLogin >= 0 && login.Value != (previousLogin is 1 or 2);
        var changedShortcut = draft.ShowShortcut != current.ShowShortcut;
        if (changedShortcut && services.SetShortcut(draft.ShowShortcut) is { } shortcutError) return shortcutError;
        if (changedLogin && services.SetLogin(login!.Value) is { } loginError)
        {
            var rollback = changedShortcut ? services.SetShortcut(current.ShowShortcut) : null;
            return rollback is null ? loginError : loginError + " " + rollback;
        }
        try { await persist(); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var rollback = new List<string>();
            if (changedLogin && services.SetLogin(previousLogin is 1 or 2) is { } error) rollback.Add(error);
            if (changedShortcut && services.SetShortcut(current.ShowShortcut) is { } keyError) rollback.Add(keyError);
            return UiText.Format("Settings could not be saved: {0}", ex.Message) + (rollback.Count > 0 ? " " + string.Join(" ", rollback) : "");
        }
    }
}
