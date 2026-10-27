using Avalonia.Controls;

namespace MacMon;

/// <summary>Visible ranking choices use stable IDs so saved sorts and table headers stay compatible.</summary>
public sealed class RankingMenu
{
    private readonly ComboBox _box;
    private readonly (int Id, string Label)[] _choices;
    private readonly int _fallback;
    private readonly (int Id, string Label)[] _headerChoices;
    private readonly int? _name;
    private bool _updating;
    private bool _all = true;
    private int _sort;
    public event Action? Changed;

    public RankingMenu(ComboBox box, (int Id, string Label)[] choices, int fallback, int? name = null, (int Id, string Label)[]? headerChoices = null)
    {
        _headerChoices = headerChoices ?? [];
        _box = box; _choices = choices; _fallback = _sort = fallback; _name = name;
        Rebuild();
        box.SelectionChanged += (_, _) =>
        {
            if (!_updating && box.SelectedItem is ComboBoxItem { Tag: int id }) Sort = id;
        };
    }
    public int Sort
    {
        get => _sort;
        set
        {
            if (_sort == value) return;
            _sort = value; Select(); Changed?.Invoke();
        }
    }
    public bool All
    {
        get => _all;
        set
        {
            if (_all == value) return;
            _all = value;
            Rebuild();
            if (!value && _name == _sort) Sort = _fallback;
        }
    }
    private void Rebuild()
    {
        _updating = true;
        try
        {
            _box.ItemsSource = _choices.Where(c => _all || c.Id != _name)
                .Select(c => new ComboBoxItem { Tag = c.Id, [!ContentControl.ContentProperty] = UiText.Bind(c.Label) }).ToArray();
        }
        finally { _updating = false; }
        Select();
    }
    private void Select()
    {
        _updating = true;
        try
        {
            _box.SelectedItem = _box.Items.OfType<ComboBoxItem>().FirstOrDefault(c => c.Tag is int id && id == _sort);
            _box.PlaceholderText = UiText.Get(_choices.Concat(_headerChoices).FirstOrDefault(c => c.Id == _sort).Label ?? "");
        }
        finally { _updating = false; }
    }
}
