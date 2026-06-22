namespace hardware_monitor.Web;

/// <summary>
/// Strongly-typed view of the "HardwareMonitor" section of appsettings.json.
/// </summary>
public class MonitorOptions
{
    public const string SectionName = "HardwareMonitor";

    /// <summary>SQLite file path (relative to the working directory, or absolute).</summary>
    public string DatabasePath { get; set; } = "hardware-monitor.db";

    /// <summary>How often persisted rows are written to SQLite. Live serial/display stays at 1 Hz.</summary>
    public int PersistIntervalSeconds { get; set; } = 5;

    /// <summary>Rows older than this many days are pruned on startup and once per day.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Drives to track capacity for, e.g. ["C:", "D:"]. Empty = the system drive only.</summary>
    public string[] Drives { get; set; } = Array.Empty<string>();

    /// <summary>Default CPU "warning" temperature limit (°C), used until the user saves one.</summary>
    public double CpuWarnThreshold { get; set; } = 75;

    /// <summary>Default CPU "critical" temperature limit (°C), used until the user saves one.</summary>
    public double CpuCritThreshold { get; set; } = 90;

    /// <summary>Default GPU "warning" temperature limit (°C), used until the user saves one.</summary>
    public double GpuWarnThreshold { get; set; } = 70;

    /// <summary>Default GPU "critical" temperature limit (°C), used until the user saves one.</summary>
    public double GpuCritThreshold { get; set; } = 85;

    /// <summary>Kestrel bind URL. Must be localhost-only.</summary>
    public string WebUrl { get; set; } = "http://localhost:5005";
}
