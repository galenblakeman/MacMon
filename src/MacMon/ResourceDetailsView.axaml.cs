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
        DetailTitle.Text = snapshot.Title; DetailStatus.Text = snapshot.Status; Definition.Text = snapshot.Definition;
        DetailRows.ItemsSource = snapshot.Rows;
        AutomationProperties.SetName(this, snapshot.Title);
    }
}
