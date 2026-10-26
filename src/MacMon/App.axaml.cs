using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace MacMon;

public sealed partial class App : Application
{
    private string _palette = "Classic";
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ActualThemeVariantChanged += (_, _) => RefreshPalette();
        ApplyAppearance("Dark", "Classic");
    }
    public void ApplyAppearance(string theme, string palette)
    {
        _palette = Appearance.NormalizePalette(palette);
        RequestedThemeVariant = Appearance.Variant(theme);
        RefreshPalette();
    }
    private void RefreshPalette() => Appearance.SetPaletteResources(Resources, _palette, ActualThemeVariant != ThemeVariant.Light);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
