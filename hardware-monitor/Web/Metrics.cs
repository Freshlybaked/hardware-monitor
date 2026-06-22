namespace hardware_monitor.Web;

/// <summary>Canonical metric names used as the <c>Metric</c> column value in SQLite.</summary>
public static class Metrics
{
    public const string CpuTemp = "cpu_temp";
    public const string GpuTemp = "gpu_temp";
    public const string RamUsedPct = "ram_used_pct";
    public const string NetDownMbps = "net_down_mbps";
    public const string NetUpMbps = "net_up_mbps";

    /// <summary>Per-drive capacity metric, e.g. drive "C" -&gt; "disk_used_pct_c".</summary>
    public static string DiskUsedPct(string driveLetter) => $"disk_used_pct_{driveLetter.ToLowerInvariant()}";
}

/// <summary>One plottable series within a group (a single metric + display label).</summary>
public record MetricSeries(string Metric, string Label);

/// <summary>A group of related series sharing a unit (e.g. Network = Down + Up, in Mbps).</summary>
public record MetricGroup(string Key, string Label, string Unit, IReadOnlyList<MetricSeries> Series);

/// <summary>
/// The set of metrics this build exposes. Built once at startup from the configured drives, then
/// shared by the API (for validation + the /metrics endpoint) and the dashboard.
/// </summary>
public class MetricCatalog
{
    public IReadOnlyList<MetricGroup> Groups { get; }
    public IReadOnlySet<string> ValidMetrics { get; }

    public MetricCatalog(IEnumerable<string> driveLetters)
    {
        var groups = new List<MetricGroup>
        {
            new("temp", "Temperature", "°C", new[]
            {
                new MetricSeries(Metrics.CpuTemp, "CPU"),
                new MetricSeries(Metrics.GpuTemp, "GPU"),
            }),
            new("ram", "RAM Usage", "%", new[]
            {
                new MetricSeries(Metrics.RamUsedPct, "RAM"),
            }),
            new("net", "Network", "Mbps", new[]
            {
                new MetricSeries(Metrics.NetDownMbps, "Down"),
                new MetricSeries(Metrics.NetUpMbps, "Up"),
            }),
        };

        var diskSeries = driveLetters
            .Select(letter => new MetricSeries(Metrics.DiskUsedPct(letter), $"{letter.ToUpperInvariant()}:"))
            .ToList();
        if (diskSeries.Count > 0)
        {
            groups.Add(new MetricGroup("disk", "Disk Usage", "%", diskSeries));
        }

        Groups = groups;
        ValidMetrics = groups.SelectMany(g => g.Series).Select(s => s.Metric).ToHashSet();
    }
}
