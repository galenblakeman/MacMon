using Avalonia;

namespace MacMon;

/// <summary>Compact dimensions never replace the saved full-window dimensions.</summary>
public sealed class WindowViewState
{
    public const double CompactHeight = 224;
    public const double CompactMinimumWidth = 720;
    public bool CompactMode { get; private set; }
    public Size FullSize { get; private set; }
    public double CompactWidth { get; private set; }
    public WindowViewState(MacPreferences preferences)
    {
        var normalized = preferences.Normalized(preferences.BootId);
        CompactMode = normalized.CompactMode;
        FullSize = new(normalized.Width, normalized.Height);
        CompactWidth = normalized.CompactWidth;
    }
    public void SetMode(bool compact) => CompactMode = compact;
    public void ObserveSize(Size size, bool normal)
    {
        if (!normal || !double.IsFinite(size.Width) || !double.IsFinite(size.Height)) return;
        if (CompactMode) CompactWidth = Math.Clamp(size.Width, CompactMinimumWidth, 2400);
        else FullSize = new(Math.Clamp(size.Width, 800, 2400), Math.Clamp(size.Height, 760, 1800));
    }
    public MacPreferences ApplyTo(MacPreferences current) => current with
    { CompactMode = CompactMode, CompactWidth = CompactWidth, Width = FullSize.Width, Height = FullSize.Height };
}
