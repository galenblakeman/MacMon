using Avalonia.Controls.Primitives;

namespace MacMon;

/// <summary>One expanded/collapsed choice shared by every data-tab action bar.</summary>
public sealed class ActionBarState
{
    private readonly ToggleButton[] _toggles;
    private bool _updating;
    public ActionBarState(params ToggleButton[] toggles)
    {
        _toggles = toggles;
        foreach (var toggle in toggles)
            toggle.IsCheckedChanged += (_, _) => { if (!_updating) Hidden = toggle.IsChecked != true; };
    }
    public bool Hidden
    {
        get => _toggles.FirstOrDefault()?.IsChecked != true;
        set
        {
            if (_updating) return;
            _updating = true;
            try
            {
                foreach (var toggle in _toggles)
                {
                    toggle.IsChecked = !value;
                    ButtonIcon.SetKind(toggle, value ? "ChevronUp" : "ChevronDown");
                }
            }
            finally { _updating = false; }
        }
    }
}
