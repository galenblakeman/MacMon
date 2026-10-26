namespace MacMon;

public sealed partial class MainWindow
{
    private ResourceGauge[] AllResourceGauges => [CpuGauge, MemoryGauge, NetworkGauge, StorageGauge, GpuGauge, SecondDiskGauge, .. CompactPanel.Gauges];
    private void InitializeResourceDetails()
    {
        foreach (var (gauge, index) in AllResourceGauges.Select((gauge, index) => (gauge, index)))
        {
            gauge.DetailsOpening += HideResourceDetails;
            var metric = (ResourceMetric)(index % ResourceCharts.Names.Length);
            gauge.EnableDetails(() => ResourceDetails.Create(metric, _session.HasLiveSample ? _session.Latest : null,
                _source.Memory, _gaugeHistory.Readings[(int)metric], Environment.ProcessorCount));
        }
    }
    private void HideResourceDetails()
    {
        foreach (var gauge in AllResourceGauges) gauge.HideDetails();
    }
}
