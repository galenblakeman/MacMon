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
    public void ActualShellUsesWinMonMappingsAndKeepsExcludedControlsTextOnly()
    {
        var main = new MainWindow();
        main.FindControl<Grid>("MainLayout")!.IsVisible = true;
        main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
        main.FindControl<WrapPanel>("ProcessActionsPanel")!.IsVisible = true;
        var shell = main.Content; main.Content = null;
        var window = new Window { Width = 1000, Height = 900, Content = shell };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var (name, kind) in new[] { ("OpenExport", "Download"), ("AlwaysOnTop", "AlwaysOnTop"),
                ("EnterCompactView", "Compact"), ("LiveMode", "Start"), ("CaptureMode", "CaptureFrame"),
                ("MoreProcessActions", "More"), ("EndProcess", "Cancel") })
            {
                var button = main.FindControl<Button>(name)!;
                var icon = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Data is not null);
                Assert.That(icon.Data, Is.SameAs(ButtonIcon.GeometryFor(kind)), name);
                Assert.That(icon.Width, Is.EqualTo(16), name);
                Assert.That(icon.StrokeLineCap, Is.EqualTo(Avalonia.Media.PenLineCap.Round));
                Assert.That(icon.StrokeJoin, Is.EqualTo(Avalonia.Media.PenLineJoin.Round));
            }
            var capture = main.FindControl<Button>("CaptureMode")!;
            var dot = capture.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
            Assert.That(dot.Width, Is.EqualTo(3)); Assert.That(dot.Fill, Is.Not.Null);
            foreach (var name in new[] { "AutomaticCaptures", "Range2", "Range10", "Range30", "Range60", "IndividualProcesses", "GroupProcesses", "ProcessCpuHeader" })
                Assert.That(main.FindControl<Button>(name)!.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any(), Is.False, name);
            var collapse = main.FindControl<ToggleButton>("ShowProcessActions")!;
            collapse.IsChecked = false; Dispatcher.UIThread.RunJobs();
            Assert.That(ButtonIcon.GetData(collapse), Is.SameAs(ButtonIcon.GeometryFor("ChevronUp")));
            collapse.IsChecked = true; Dispatcher.UIThread.RunJobs();
            Assert.That(ButtonIcon.GetData(collapse), Is.SameAs(ButtonIcon.GeometryFor("ChevronDown")));
            var export = new ExportDialog();
            Assert.That(ButtonIcon.GetKind(export.FindControl<Button>("ExportCsv")!), Is.EqualTo("Save"));
            export.Close();
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RowPinsUseWinMonOutlineAndFilledShapesWhenContentChanges()
    {
        var pin = new Button { Content = "★", Classes = { "rowPin" } };
        var window = new Window { Content = pin, Width = 200, Height = 100 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var filled = pin.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            Assert.That(filled.Data, Is.SameAs(ButtonIcon.GeometryFor("PinFilled")));
            Assert.That(filled.Fill, Is.Not.Null);
            pin.Content = "☆"; Dispatcher.UIThread.RunJobs();
            var outline = pin.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            Assert.That(outline.Data, Is.SameAs(ButtonIcon.GeometryFor("Pin")));
            Assert.That(outline.Fill, Is.Null);
        }
        finally { window.Close(); }
    }

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
        ButtonIcon.SetKind(save, "Save"); ButtonIcon.SetKind(toggle, "More"); ButtonIcon.SetKind(disabled, "Copy");
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
