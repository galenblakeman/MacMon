using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class TableToolbarLayoutTests
{
    [AvaloniaTest]
    public void SearchFillsEachToolbarAndTrailingControlsStayRightAnchoredAcrossResizes()
    {
        var main = new MainWindow();
        main.FindControl<Grid>("MainLayout")!.IsVisible = true;
        main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
        var shell = (Control)main.Content!; main.Content = null;
        var window = new Window { Width = 1100, Height = 800, Content = shell };
        try
        {
            window.Show();
            foreach (var language in new[] { "en", "es" })
            foreach (var tab in new[] { 0, 1, 2 })
            {
                UiText.Apply(language);
                main.FindControl<TabControl>("DataTabs")!.SelectedIndex = tab;
                Dispatcher.UIThread.RunJobs();
                var toolbar = shell.GetVisualDescendants().OfType<TableToolbarLayout>().Single(t => t.IsEffectivelyVisible);
                var search = (TextBox)toolbar.Children[1]; var actions = toolbar.Children[2];
                double previousWidth = 0;
                foreach (var width in new[] { 800, 1100, 1400, 800 })
                {
                    window.Width = width; Dispatcher.UIThread.RunJobs();
                    Assert.That(search.Bounds.Width, Is.GreaterThanOrEqualTo(140));
                    Assert.That(actions.Bounds.Right, Is.EqualTo(toolbar.Bounds.Width).Within(1), $"{language}, tab {tab}, width {width}");
                    Assert.That(search.Bounds.Right + search.Margin.Right, Is.EqualTo(actions.Bounds.Left).Within(1));
                    Assert.That(search.Bounds.Bottom, Is.LessThanOrEqualTo(toolbar.Bounds.Height));
                    if (width == 1400) Assert.That(search.Bounds.Width - previousWidth, Is.EqualTo(300).Within(1), "Extra width belongs to search");
                    previousWidth = search.Bounds.Width;
                    foreach (var child in toolbar.Children)
                    {
                        Assert.That(child.Bounds.Left, Is.GreaterThanOrEqualTo(0));
                        Assert.That(child.Bounds.Right, Is.LessThanOrEqualTo(toolbar.Bounds.Width + 1));
                    }
                }
            }
        }
        finally { window.Close(); UiText.Apply("en"); }
    }
}
