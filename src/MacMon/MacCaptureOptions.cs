using WinMon.Monitoring;

namespace MacMon;

public sealed record MacCaptureOptions(bool Enabled, IReadOnlyList<CaptureRuleSetting> Rules, int CooldownSeconds = 60)
{
    public static CaptureMetric[] SupportedMetrics => [CaptureMetric.Cpu, CaptureMetric.Memory, CaptureMetric.Network, CaptureMetric.Process];
    public static MacCaptureOptions Default => new(false,
        CaptureRuleSetting.Defaults.Where(rule => SupportedMetrics.Contains(rule.Metric)).ToArray());
    public MacCaptureOptions Normalized() => new(Enabled, SupportedMetrics.Select(metric =>
        (Rules?.FirstOrDefault(rule => rule is not null && rule.Metric == metric) ??
         Default.Rules.First(rule => rule.Metric == metric)).Normalized()).ToArray(), Math.Clamp(CooldownSeconds, 1, 3600));
}
