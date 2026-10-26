using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace MacMon;

public sealed partial class App : Application
{
    private string _palette = "Classic";
    private string _theme = "Dark";
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ActualThemeVariantChanged += (_, _) => RefreshPalette();
        ApplyAppearance("Dark", "Classic");
    }
    public void ApplyAppearance(string theme, string palette, string gaugeStyle = "Bowl")
    {
        _theme = Appearance.NormalizeTheme(theme);
        _palette = Appearance.NormalizePalette(palette);
        Resources["ResourceGaugeStyle"] = ResourceGauge.NormalizeStyle(gaugeStyle);
        RequestedThemeVariant = Appearance.Variant(theme);
        RefreshPalette();
    }
    private void RefreshPalette()
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        Appearance.SetThemeResources(Resources, _theme, dark);
        Appearance.SetPaletteResources(Resources, _palette, dark, _theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
