using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;

namespace MacMon;

/// <summary>Localized title/navigation captions using the shared WinMon icon catalog.</summary>
public sealed class TitleButtonContentExtension(string icon, string label) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var caption = UiText.Label(label); caption.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { ButtonIcon.Create(icon, caption), caption }
        };
    }
}
