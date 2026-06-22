using System.Text.Json;

namespace hardware_monitor.Web;

/// <summary>
/// User-configured alert limits (°C) for CPU and GPU temperature. Each component has a "warning" and
/// a "critical" level (warn &lt; crit). Persisted in SQLite and pushed to the display so it can warn
/// when a reading crosses a level. The host itself does not alert.
/// </summary>
public record Thresholds(double CpuWarn, double CpuCrit, double GpuWarn, double GpuCrit)
{
    /// <summary>
    /// The versioned "cfg" line sent to the display, e.g.
    /// {"v":1,"t":"cfg","cpu_warn":75,"cpu_crit":90,"gpu_warn":70,"gpu_crit":85}.
    /// The <c>t</c> field distinguishes this from the 1 Hz telemetry ("data") line. No trailing
    /// newline — SerialWriter appends "\n", UDP frames per datagram.
    /// </summary>
    public string ToConfigPayload() =>
        JsonSerializer.Serialize(new
        {
            v = 1,
            t = "cfg",
            cpu_warn = CpuWarn,
            cpu_crit = CpuCrit,
            gpu_warn = GpuWarn,
            gpu_crit = GpuCrit
        });
}
