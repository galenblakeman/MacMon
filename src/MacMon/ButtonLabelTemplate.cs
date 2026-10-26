using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace MacMon;

/// <summary>Decorates explicitly mapped buttons without replacing captions, bindings or accessible names.</summary>
public sealed class ButtonLabelTemplate : IDataTemplate
{
    public bool Match(object? data) => data is string or Control;
    public Control? Build(object? data) => data is Control control ? control : data is string text ? new ButtonLabel(text) : null;

    private sealed class ButtonLabel : Grid
    {
        private readonly TextBlock _caption;
        private readonly bool _pinned;
        public ButtonLabel(string text)
        {
            _pinned = text == "★";
            ColumnDefinitions = new("Auto,*");
            VerticalAlignment = VerticalAlignment.Center;
            _caption = new() { Text = text.TrimStart('●', '◎', 'ⓘ', '↻', '☆', ' '),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            SetColumn(_caption, 1); Children.Add(_caption);
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var button = this.FindAncestorOfType<Button>();
            if (button is null || ButtonIcon.GetKind(button) is not { } kind) return;
            var pin = button.Classes.Contains("rowPin");
            if (pin) { kind = _pinned ? "PinFilled" : "Pin"; _caption.IsVisible = false; }
            if (Children.Count == 1) Children.Insert(0, ButtonIcon.Create(kind, _caption, pin ? 1.3 : 1.5));
            ColumnSpacing = pin ? 0 : 6;
        }
    }
}
