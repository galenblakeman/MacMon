using Avalonia;
using Avalonia.Controls;

namespace MacMon;

/// <summary>Ranking choices, expanding search, and right-anchored filters/actions.</summary>
public sealed class TableToolbarLayout : Grid
{
    private bool? _wide;
    public TableToolbarLayout()
    {
        ColumnDefinitions = new("Auto,*,Auto"); RowDefinitions = new("Auto,Auto");
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 3 && double.IsFinite(availableSize.Width))
        {
            var ranking = Children[0]; var search = Children[1]; var actions = Children[2];
            ranking.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            actions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var wide = availableSize.Width >= ranking.DesiredSize.Width + actions.DesiredSize.Width + search.MinWidth + search.Margin.Left + search.Margin.Right;
            if (_wide != wide)
            {
                _wide = wide;
                SetColumnSpan(ranking, wide ? 1 : 3);
                SetColumn(search, wide ? 1 : 0); SetColumnSpan(search, wide ? 1 : 2);
                SetRow(search, wide ? 0 : 1); SetColumn(actions, 2); SetRow(actions, wide ? 0 : 1);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
