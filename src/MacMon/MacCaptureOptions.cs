using WinMon.Monitoring;

namespace MacMon;

public sealed record MacCaptureOptions(bool Enabled, IReadOnlyList<CaptureRuleSetting> Rules, int CooldownSeconds = 60,
    string? Folder = null, bool Notify = false)
{
    public string ReportFolder => Folder ?? Path.Combine(MacPaths.AppSupport, "Captures");
    public static CaptureMetric[] SupportedMetrics => [CaptureMetric.Cpu, CaptureMetric.Gpu, CaptureMetric.Memory, CaptureMetric.Network, CaptureMetric.Process];
    public static MacCaptureOptions Default => new(false,
        CaptureRuleSetting.Defaults.Where(rule => SupportedMetrics.Contains(rule.Metric)).ToArray());
    public MacCaptureOptions Normalized() => new(Enabled, SupportedMetrics.Select(metric =>
        (Rules?.FirstOrDefault(rule => rule is not null && rule.Metric == metric) ??
         Default.Rules.First(rule => rule.Metric == metric)).Normalized()).ToArray(), Math.Clamp(CooldownSeconds, 1, 3600),
         NormalizeFolder(Folder), Notify);
    internal static string? NormalizeFolder(string? folder) => string.IsNullOrWhiteSpace(folder) || folder.Length > 4096 ||
        folder.Any(char.IsControl) || !Path.IsPathFullyQualified(folder) ? null : Path.TrimEndingDirectorySeparator(folder);
}
