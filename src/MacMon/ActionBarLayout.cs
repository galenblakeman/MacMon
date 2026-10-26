using Avalonia;
using Avalonia.Controls;

namespace MacMon;

/// <summary>Selection on the left, actions on the right; actions move below at narrow widths.</summary>
public sealed class ActionBarLayout : Grid
{
    private bool? _wide;
    public ActionBarLayout()
    {
        ColumnDefinitions = new("*,Auto"); RowDefinitions = new("Auto,Auto");
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 2 && double.IsFinite(availableSize.Width))
        {
            var actions = Children[1];
            actions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var wide = !actions.IsVisible || availableSize.Width >= actions.DesiredSize.Width + 240;
            if (_wide != wide)
            {
                _wide = wide;
                ColumnDefinitions[1].Width = wide ? GridLength.Auto : new GridLength(0);
                SetColumn(actions, wide ? 1 : 0); SetRow(actions, wide ? 0 : 1);
                SetColumnSpan(actions, wide ? 1 : 2);
                actions.Margin = wide ? default : new Thickness(0, 6, 0, 0);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
