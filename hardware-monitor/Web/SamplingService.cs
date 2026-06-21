using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace hardware_monitor.Web;

/// <summary>
/// Runs the live sampling loop at ~1 Hz: reads CPU/GPU temps, records OpenTelemetry metrics, and
/// sends the formatted payload to the ESP32 display over serial (preferred) or UDP — exactly the
/// behaviour that previously lived in Program.cs. It additionally publishes each reading to
/// <see cref="LatestReadings"/> so the persistence service and web API can observe the same stream.
/// </summary>
public class SamplingService : BackgroundService
{
    private readonly ISensorRetriever _sensors;
    private readonly DisplaySender _display;
    // private readonly TelemetrySender _telemetry;
    private readonly LatestReadings _latest;
    private readonly ILogger<SamplingService> _logger;

    public SamplingService(
        ISensorRetriever sensors,
        DisplaySender display,
        // TelemetrySender telemetry,
        LatestReadings latest,
        ILogger<SamplingService> logger)
    {
        _sensors = sensors;
        _display = display;
        // _telemetry = telemetry;
        _latest = latest;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sampling loop started (1 Hz)");

        while (!stoppingToken.IsCancellationRequested)
        {
            int cpuTemp = _sensors.GetCPUTemp();
            int gpuTemp = _sensors.GetGPUTemp();
            double ramUsed = _sensors.GetRamUsedPercent();
            var (downMbps, upMbps) = _sensors.GetNetworkThroughput();
            IReadOnlyList<DriveUsage> drives = _sensors.GetStorageUsage();

            var readings = new Dictionary<string, double>
            {
                [Metrics.CpuTemp] = cpuTemp,
                [Metrics.GpuTemp] = gpuTemp,
                [Metrics.RamUsedPct] = ramUsed,
                [Metrics.NetDownMbps] = downMbps,
                [Metrics.NetUpMbps] = upMbps,
            };
            foreach (DriveUsage drive in drives)
            {
                readings[Metrics.DiskUsedPct(drive.Letter)] = drive.UsedPercent;
            }

            // _telemetry.RecordTemperatures(cpuTemp, gpuTemp);
            _latest.Update(readings);

            string payload = CreatePayload(cpuTemp, gpuTemp, ramUsed, downMbps, upMbps, drives);
            Console.WriteLine($"Payload: {payload}");

            await _display.SendAsync(payload);

            // send updates every second
            try
            {
                await Task.Delay(1000, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Sampling loop stopped");
    }

    /// <summary>
    /// Builds the versioned JSON telemetry line sent to the display, e.g.
    /// {"v":1,"t":"data","cpu":53,"gpu":48,"ram":41,"net":{"dn":12.3,"up":0.4},"disk":{"C":38,"D":72.4}}.
    /// The "t" field marks this as telemetry ("data") vs the threshold ("cfg") line.
    /// Self-describing so the firmware reads only the keys it renders; "disk" carries one key per
    /// tracked drive. No trailing newline — SerialWriter adds "\n", UDP frames per datagram.
    /// </summary>
    private static string CreatePayload(int cpu, int gpu, double ram,
                                        double netDn, double netUp,
                                        IReadOnlyList<DriveUsage> drives)
    {
        var payload = new
        {
            v = 1,
            t = "data",
            cpu,
            gpu,
            ram,
            net = new { dn = netDn, up = netUp },
            disk = drives.ToDictionary(d => d.Letter, d => d.UsedPercent),
        };
        return JsonSerializer.Serialize(payload);
    }
}
