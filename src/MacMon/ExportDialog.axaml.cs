using Avalonia.Controls;
using WinMon.Monitoring;

namespace MacMon;

public sealed record ExportSelection(CsvExportKind Kind, bool ChartRange);
public sealed partial class ExportDialog : Window
{
    public static CsvExportKind InitialKind(bool compact, int tab) => compact || tab == 1 ? CsvExportKind.ResourceHistory :
        tab == 2 ? CsvExportKind.UsageTotals : CsvExportKind.ProcessSamples;
    public ExportDialog() : this(CsvExportKind.ResourceHistory) { }
    public ExportDialog(CsvExportKind initialKind)
    {
        InitializeComponent();
        UsageChoice.IsCheckedChanged += (_, _) => UpdateChoice();
        CancelAction.Click += (_, _) => Close();
        ExportCsv.Click += (_, _) => Close(Selection);
        ResourceChoice.IsChecked = initialKind == CsvExportKind.ResourceHistory;
        ProcessChoice.IsChecked = initialKind == CsvExportKind.ProcessSamples;
        UsageChoice.IsChecked = initialKind == CsvExportKind.UsageTotals;
        UpdateChoice();
    }
    public ExportSelection Selection => new(UsageChoice.IsChecked == true ? CsvExportKind.UsageTotals :
        ProcessChoice.IsChecked == true ? CsvExportKind.ProcessSamples : CsvExportKind.ResourceHistory, RangeChoice.IsChecked == true);
    private void UpdateChoice()
    {
        TimeChoices.IsVisible = UsageChoice.IsChecked != true;
        DataNote.Text = UiText.Get(UsageChoice.IsChecked == true ?
            "Usage includes the entire session, including apps hidden by table filters." :
            "Exports include retained samples regardless of table filters. Choose a CSV file in the next step.");
    }
}
