using System.Text.Json;

namespace hardware_monitor.Web;

/// <summary>
/// User-configured alert limits (°C) for CPU and GPU temperature. Persisted in SQLite and pushed to
/// the display so it can warn when a reading crosses a limit. The host itself does not alert.
/// </summary>
public record Thresholds(double Cpu, double Gpu)
{
    /// <summary>
    /// The versioned "cfg" line sent to the display, e.g. {"v":1,"t":"cfg","cpu":80,"gpu":75}.
    /// The <c>t</c> field distinguishes this from the 1 Hz telemetry ("data") line. No trailing
    /// newline — SerialWriter appends "\n", UDP frames per datagram.
    /// </summary>
    public string ToConfigPayload() =>
        JsonSerializer.Serialize(new { v = 1, t = "cfg", cpu = Cpu, gpu = Gpu });
}
