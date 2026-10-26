using Avalonia.Automation;
using Avalonia.Controls;

namespace MacMon;

public static class CaptureToolbar
{
    public static void Update(Button button, bool enabled, int count)
    {
        button.Content = count == 0 ? UiText.Get("Auto") : UiText.Format("Auto {0}", count);
        var status = UiText.Format(enabled
            ? "Automatic captures enabled · Captures in history: {0}"
            : "Automatic captures disabled · Captures in history: {0}", count);
        ToolTip.SetTip(button, status);
        AutomationProperties.SetName(button, status);
    }
}
