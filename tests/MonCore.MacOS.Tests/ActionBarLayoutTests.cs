using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class ActionBarLayoutTests
{
    [AvaloniaTest]
    public void ActionsMoveBelowIdentityWhenNarrowAndRespondToSelectionVisibility()
    {
        var identity = new TextBlock { Text = "Selected process", MinHeight = 40 };
        var actions = new WrapPanel { Children = { new Button { Content = "Properties", Width = 120 }, new Button { Content = "Copy details", Width = 120 } }, IsVisible = false };
        var panel = new ActionBarLayout { Children = { identity, actions } };
        var window = new Window { Width = 400, Height = 180, Content = panel };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            actions.IsVisible = true; Dispatcher.UIThread.RunJobs();
            Assert.That(Grid.GetRow(actions), Is.EqualTo(1));
            Assert.That(actions.Bounds.Y, Is.GreaterThanOrEqualTo(identity.Bounds.Bottom));
            window.Width = 800; Dispatcher.UIThread.RunJobs();
            Assert.That(Grid.GetRow(actions), Is.Zero);
            Assert.That(actions.Bounds.X, Is.GreaterThanOrEqualTo(identity.Bounds.Right));
            Assert.That(actions.Bounds.Right, Is.LessThanOrEqualTo(panel.Bounds.Width));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void TitleBackgroundCoversInsetsAndCompactViewForEveryFinish()
    {
        var main = new MainWindow();
        var shell = main.Content; main.Content = null;
        var window = new Window { Width = 800, Height = 760, Content = shell };
        try
        {
            window.Show();
            foreach (var theme in Appearance.Themes)
            foreach (var compact in new[] { false, true })
            {
                ((App)Avalonia.Application.Current!).ApplyAppearance(theme, "Classic");
                main.FindControl<Grid>("MainLayout")!.IsVisible = !compact;
                main.FindControl<CompactView>("CompactPanel")!.IsVisible = compact;
                Dispatcher.UIThread.RunJobs();
                var background = main.FindControl<Border>("TitleBackground")!;
                Assert.That(background.Bounds, Is.EqualTo(new Rect(0, 0, 800, 44)), theme);
                Assert.That(background.Background, Is.EqualTo(Avalonia.Application.Current.Resources["MacChromeBrush"]));
            }
        }
        finally { window.Close(); }
    }
}
