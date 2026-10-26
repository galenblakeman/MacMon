using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace MacMon;

/// <summary>Resolution-independent title actions, with readable labels and consistent icon sizing.</summary>
public sealed class TitleButtonContentExtension(string icon, string label) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var path = icon switch
        {
            "Info" => "M8,1 A7,7 0 1 1 7.99,1 M8,7 L8,12 M8,4 L8,5",
            "Settings" => "M2,4 L14,4 M2,8 L14,8 M2,12 L14,12 M5,2 L5,6 M11,6 L11,10 M6,10 L6,14",
            "Appearance" => "M8,1 A7,7 0 1 0 15,8 L10,8 L10,5 L8,5 Z M5,4 L5,5 M3,8 L4,8 M6,12 L7,12",
            "Monitoring" => "M1,9 L4,9 L6,3 L9,13 L11,7 L15,7",
            "Capture" => "M1,4 L5,4 L6,2 L10,2 L11,4 L15,4 L15,14 L1,14 Z M11,9 A3,3 0 1 1 5,9 A3,3 0 1 1 11,9",
            "Export" => "M8,11 L8,1 M4,5 L8,1 L12,5 M2,10 L2,14 L14,14 L14,10",
            "Pin" => "M5,1 L11,1 L10,6 L13,9 L9,9 L8,15 L7,9 L3,9 L6,6 Z",
            "Full" => "M1,6 L1,1 L6,1 M10,1 L15,1 L15,6 M1,10 L1,15 L6,15 M10,15 L15,15 L15,10",
            _ => "M1,1 L15,1 L15,15 L1,15 Z M6,6 L14,6 L14,14 L6,14 Z"
        };
        var caption = UiText.Label(label); caption.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Stroke = Brushes.Gray,
                    StrokeThickness = 1.5, Width = 16, Height = 16, Stretch = Stretch.Uniform,
                    [!Avalonia.Controls.Shapes.Shape.StrokeProperty] = new Avalonia.Data.Binding("Foreground") { Source = caption } },
                caption
            }
        };
    }
}
