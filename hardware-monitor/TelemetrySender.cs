using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

public class TelemetrySender : IDisposable
{
    private volatile int _lastCpuTemp;
    private volatile int _lastGpuTemp;

    private readonly Meter _meter;
    private readonly MeterProvider _meterProvider;

    public TelemetrySender()
    {
        _meter = new Meter("HardwareMonitor", "1.0.0");
        _meter.CreateObservableGauge("cpu.temperature", () => _lastCpuTemp, "C", "CPU temperature in celsius");
        _meter.CreateObservableGauge("gpu.temperature", () => _lastGpuTemp, "C", "GPU temperature in celsius");

        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(r => r.AddService("hardware-monitor"))
            .AddMeter("HardwareMonitor")
            .AddOtlpExporter((exporterOptions, readerOptions) =>
            {
                exporterOptions.Endpoint = new Uri("http://localhost:4318/v1/metrics");
                exporterOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
            })
            .Build()!;
    }

    public void RecordTemperatures(int cpuTemp, int gpuTemp)
    {
        _lastCpuTemp = cpuTemp;
        _lastGpuTemp = gpuTemp;
    }

    public void Dispose()
    {
        _meterProvider.Dispose();
        _meter.Dispose();
    }
}
