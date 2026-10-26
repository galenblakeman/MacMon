using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class ButtonLayoutTests
{
    [AvaloniaTest]
    public void IconsAndCaptionsStayCenteredAcrossActionsChipsAndLanguageChanges()
    {
        var save = new Button { Name = "SaveSettings", Height = 44, [!Button.ContentProperty] = UiText.Bind("Save") };
        var chip = new RadioButton { Name = "Range30", Content = "30m", Classes = { "chip" }, IsChecked = true, Height = 36 };
        var group = new RadioButton { Name = "GroupProcesses", Content = "Grouped", Classes = { "chip" }, GroupName = "Grouping", IsChecked = true, Height = 36 };
        var toggle = new ToggleButton { Name = "ShowProcessActions", Content = "Actions", Height = 42 };
        var disabled = new Button { Name = "CopyDetails", Content = "Copy details", IsEnabled = false, Height = 40 };
        var title = new Button { Name = "OpenInfo", Height = 38,
            Content = new TitleButtonContentExtension("Info", "Info").ProvideValue(null!) };
        var window = new Window { Width = 500, Height = 300,
            Content = new StackPanel { Children = { save, chip, group, toggle, disabled, title } } };
        try
        {
            window.Show();
            foreach (var language in new[] { "en", "es" })
            {
                UiText.Apply(language); Dispatcher.UIThread.RunJobs();
                Assert.That(save.Content, Is.EqualTo(UiText.Get("Save")));
                foreach (var button in new Button[] { save, chip, group, toggle, disabled, title })
                {
                    var icons = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Where(p => p.Data is not null).ToArray();
                    Assert.That(icons.Length, Is.EqualTo(button == chip || button == group ? 0 : 1), button.Name);
                    var text = button.GetVisualDescendants().OfType<TextBlock>().Single(t => !string.IsNullOrEmpty(t.Text));
                    Assert.That(button.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Center));
                    foreach (var element in icons.Cast<Control>().Append(text))
                    {
                        var center = element.TranslatePoint(new Point(element.Bounds.Width / 2, element.Bounds.Height / 2), button)!.Value;
                        Assert.That(center.Y, Is.EqualTo(button.Bounds.Height / 2).Within(1), button.Name);
                    }
                }
            }
            toggle.Content = "Changed action"; Dispatcher.UIThread.RunJobs();
            Assert.That(toggle.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Changed action"), Is.True);
            Assert.That(disabled.IsEnabled, Is.False);
        }
        finally { window.Close(); UiText.Apply("en"); }
    }
}
