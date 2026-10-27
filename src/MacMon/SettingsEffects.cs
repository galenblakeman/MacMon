namespace MacMon;

public interface IDesktopSettingsServices
{
    string? SetShortcut(string id);
    Task<bool> RequestNotifications();
}

public sealed class NativeDesktopSettingsServices : IDesktopSettingsServices
{
    public string? SetShortcut(string id) => MacDesktopIntegration.SetShortcut(id);
    public Task<bool> RequestNotifications() => MacDesktopIntegration.RequestNotifications();
}

/// <summary>Apply native draft changes only on Save; recover native state if persistence fails.</summary>
public static class SettingsEffects
{
    public static async Task<string?> Save(MacPreferences current, MacPreferences draft,
        IDesktopSettingsServices services, Func<Task> persist)
    {
        if (draft.Captures.Notify && !current.Captures.Notify && !await services.RequestNotifications())
            return "Notifications were not allowed. Enable them in macOS Settings, or turn off capture notifications.";
        var changedShortcut = draft.ShowShortcut != current.ShowShortcut;
        if (changedShortcut && services.SetShortcut(draft.ShowShortcut) is { } shortcutError) return shortcutError;
        try { await persist(); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var rollback = new List<string>();
            if (changedShortcut && services.SetShortcut(current.ShowShortcut) is { } keyError) rollback.Add(keyError);
            return UiText.Format("Settings could not be saved: {0}", ex.Message) + (rollback.Count > 0 ? " " + string.Join(" ", rollback) : "");
        }
    }
}
