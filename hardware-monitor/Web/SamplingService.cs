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
    private readonly SerialWriter _serial;
    private readonly bool _serialAvailable;
    private readonly UdpSender _udp;
    private readonly bool _udpAvailable;
    // private readonly TelemetrySender _telemetry;
    private readonly LatestReadings _latest;
    private readonly ILogger<SamplingService> _logger;

    public SamplingService(
        ISensorRetriever sensors,
        SerialWriter serial,
        bool serialAvailable,
        UdpSender udp,
        bool udpAvailable,
        // TelemetrySender telemetry,
        LatestReadings latest,
        ILogger<SamplingService> logger)
    {
        _sensors = sensors;
        _serial = serial;
        _serialAvailable = serialAvailable;
        _udp = udp;
        _udpAvailable = udpAvailable;
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

            // _telemetry.RecordTemperatures(cpuTemp, gpuTemp);
            _latest.Update(cpuTemp, gpuTemp);

            string payload = CreatePayload(cpuTemp, gpuTemp);
            Console.WriteLine($"Payload: {payload}");

            // we prioritise sending over serial port
            if (_serialAvailable)
            {
                Console.WriteLine($"Sending payload {payload} over serial");
                _serial.SendMessage(payload);
            }
            else if (_udpAvailable)
            {
                Console.WriteLine($"Sending payload {payload} over udp");
                await _udp.SendMessage(payload);
            }

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

    private static string CreatePayload(int cpuTemp, int gpuTemp)
    {
        string cpuStr = cpuTemp.ToString();
        if (cpuTemp < 10)
        {
            cpuStr = "0" + cpuStr;
        }

        string gpuStr = gpuTemp.ToString();
        if (gpuTemp < 10)
        {
            gpuStr = "0" + gpuStr;
        }

        return cpuStr + ":" + gpuStr;
    }
}
