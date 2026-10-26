using Avalonia.Controls;

namespace MacMon;

public sealed partial class JobWorkersWindow : Window
{
    public JobWorkersWindow() : this("", DateTimeOffset.Now, new(null, false), false) { }
    public JobWorkersWindow(string label, DateTimeOffset observedAt, JobWorkerTree tree, bool historical)
    {
        InitializeComponent(); JobTitle.Text = label;
        Observation.Text = UiText.Get(historical ? "Captured" : "Live") + $" · {observedAt:yyyy-MM-dd HH:mm:ss zzz}";
        WorkerTree.ItemsSource = tree.Root is null ? Array.Empty<JobWorkerNode>() : [tree.Root];
        if (tree.Truncated) TreeNote.Text += " " + UiText.Get("Tree truncated at the node/depth limit.");
        CloseWorkers.Click += (_, _) => Close();
    }
}
