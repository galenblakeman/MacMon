using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using WinMon.Monitoring;

namespace MacMon;

public static class TableInteractions
{
    public static void AddSearchClear(TextBox search)
    {
        var clear = new Button { Classes = { "titleAction" }, Padding = new(6, 0), MinHeight = 24,
            VerticalAlignment = VerticalAlignment.Center };
        ButtonIcon.SetKind(clear, "Cancel");
        clear.ContentTemplate = null; clear.Content = ButtonIcon.Create("Cancel", clear);
        AutomationProperties.SetName(clear, UiText.Get("Clear search"));
        ToolTip.SetTip(clear, UiText.Get("Clear search"));
        void Refresh() => clear.IsVisible = !string.IsNullOrEmpty(search.Text);
        clear.Click += (_, _) => { search.Text = ""; search.Focus(); };
        search.TextChanged += (_, _) => Refresh();
        search.InnerRightContent = clear; Refresh();
    }
    public static bool Matches(ProcessUsage process, string query)
    {
        query = query.Trim();
        return query.StartsWith("path:", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(process.ImagePath, query[5..], StringComparison.Ordinal)
            : ProcessSearch.Filter([process], query).Any();
    }
    public static MenuItem Item(string title, string icon, Func<Task> action, bool enabled = true)
    {
        var item = new MenuItem { Header = UiText.Get(title), IsEnabled = enabled };
        item.Icon = ButtonIcon.Create(icon, item);
        item.Click += async (_, _) => await action();
        return item;
    }
}
