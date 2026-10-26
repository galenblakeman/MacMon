using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class InputLayoutTests
{
    [AvaloniaTest]
    public void SingleLineInputsCenterSelectedTextTypedTextAndPlaceholdersAtDifferentHeights()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Selected option" }, SelectedIndex = 0 };
        var ranking = new ComboBox { ItemsSource = new[] { "CPU" }, SelectedIndex = 0, Classes = { "rankingChoice" } };
        var text = new TextBox { Text = "Search name or PID" };
        var empty = new TextBox { PlaceholderText = "Search name or PID" };
        var readOnly = new TextBox { Text = "/Users/Shared/Captures", IsReadOnly = true };
        var number = new NumericUpDown { Value = 75, ShowButtonSpinner = false };
        var controls = new Control[] { combo, ranking, text, empty, readOnly, number };
        var panel = new StackPanel(); foreach (var control in controls) panel.Children.Add(control);
        var window = new Window { Width = 400, Height = 350, Content = panel };
        try
        {
            window.Show();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var height in new[] { 28, 32, 40 })
            {
                window.RequestedThemeVariant = theme;
                foreach (var control in controls) control.Height = height;
                Dispatcher.UIThread.RunJobs();
                AssertCentered(combo); AssertCentered(ranking);
                foreach (var control in controls.Skip(2)) AssertCentered(control);
                // Empty search fields must use the same position as entered text.
                var placeholder = empty.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder");
                AssertCenter(empty, placeholder);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void ActualSettingsInputsCenterAcrossSectionsAndLanguages()
    {
        var window = new SettingsWindow(new MacPreferences(), [new("", "Automatic")], 0, true,
            _ => Task.FromResult<string?>(null));
        try
        {
            window.Show();
            foreach (var language in new[] { "en", "es" })
            foreach (var section in new[] { 0, 1, 2, 3 })
            {
                UiText.Apply(language);
                window.FindControl<ListBox>("Sections")!.SelectedIndex = section;
                Dispatcher.UIThread.RunJobs();
                var inputs = window.GetVisualDescendants().OfType<Control>()
                    .Where(c => c.IsEffectivelyVisible && c is (ComboBox or NumericUpDown or TextBox)).ToArray();
                Assert.That(inputs, Is.Not.Empty);
                foreach (var control in inputs) AssertCentered(control);
            }
            var detailsWindow = new ProcessDialog("Process details", "First line\nSecond line");
            var details = detailsWindow.FindControl<TextBox>("Details")!;
            try
            {
                detailsWindow.Show(); Dispatcher.UIThread.RunJobs();
                var presenter = details.GetVisualDescendants().OfType<TextPresenter>().Single();
                Assert.That(presenter.TranslatePoint(new Point(), details)!.Value.Y, Is.LessThan(10));
                Assert.That(presenter.Bounds.Height, Is.LessThan(details.Bounds.Height / 2));
            }
            finally { detailsWindow.Close(); }
        }
        finally { window.Close(); UiText.Apply("en"); }
    }

    private static void AssertCentered(Control input)
    {
        var content = input is ComboBox
            ? input.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Cast<Control>().ToArray()
            : input.GetVisualDescendants().OfType<TextPresenter>().Cast<Control>().ToArray();
        Assert.That(content, Is.Not.Empty, input.Name ?? input.GetType().Name);
        foreach (var element in content) AssertCenter(input, element);
    }

    private static void AssertCenter(Control input, Control content)
    {
        var center = content.TranslatePoint(new Point(0, content.Bounds.Height / 2), input)!.Value.Y;
        Assert.That(center, Is.EqualTo(input.Bounds.Height / 2).Within(1), $"{input.Name ?? input.GetType().Name}: {content.Name}");
    }
}
