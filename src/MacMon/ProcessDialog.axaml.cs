using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace MacMon;

public sealed partial class ProcessDialog : Window
{
    public ProcessDialog() : this("Process details", "") { }
    public ProcessDialog(string title, string details, bool confirmation = false)
    {
        InitializeComponent(); Title = UiText.Get(title); Details.Text = details;
        ConfirmAction.IsVisible = confirmation;
        CancelAction.Content = UiText.Get(confirmation ? "Cancel" : "Close");
        CancelAction.Click += (_, _) => Close(false);
        ConfirmAction.Click += (_, _) => Close(true);
        CopyDetails.Click += async (_, _) => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(details); };
    }
}
