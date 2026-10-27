using Avalonia.Automation;
using Avalonia.Controls;

namespace MacMon;

public sealed partial class ResourceDetailsView : UserControl
{
    public event Action? CloseRequested;
    public ResourceDetailsView()
    {
        InitializeComponent();
        CloseDetails.Click += (_, _) => CloseRequested?.Invoke();
    }
    public void Update(ResourceDetailSnapshot snapshot)
    {
        DetailTitle.Text = UiText.Get(snapshot.Title); DetailStatus.Text = UiText.Get(snapshot.Status); Definition.Text = UiText.Get(snapshot.Definition);
        DetailRows.ItemsSource = snapshot.Rows.Select(row => row with { Label = UiText.Get(row.Label) }).ToArray();
        AutomationProperties.SetName(this, UiText.Get(snapshot.Title));
    }
}
