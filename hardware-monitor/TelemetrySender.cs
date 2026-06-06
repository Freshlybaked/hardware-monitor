using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

public class TelemetrySender : IDisposable
{
    private static readonly Meter Meter = new("HardwareMonitor", "1.0.0");
    private static readonly Gauge<int> CpuTempGauge = Meter.CreateGauge<int>("cpu.temperature", "C", "CPU temperature in celsius");
    private static readonly Gauge<int> GpuTempGauge = Meter.CreateGauge<int>("gpu.temperature", "C", "GPU temperature in celsius");

    private readonly MeterProvider meterProvider;

    public TelemetrySender()
    {
        meterProvider = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(r => r.AddService("hardware-monitor"))
            .AddMeter("HardwareMonitor")
            .AddOtlpExporter(opts =>
            {
                opts.Endpoint = new Uri("http://localhost:4318");
                opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            })
            .Build()!;
    }

    public void RecordTemperatures(int cpuTemp, int gpuTemp)
    {
        CpuTempGauge.Record(cpuTemp);
        GpuTempGauge.Record(gpuTemp);
    }

    public void Dispose()
    {
        meterProvider.Dispose();
    }
}
