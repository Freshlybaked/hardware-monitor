using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

public class TelemetrySender : IDisposable
{
    private readonly object _lock = new();
    private readonly List<int> _cpuSamples = new();
    private readonly List<int> _gpuSamples = new();

    private double _cpuAvg, _cpuStdDev, _gpuAvg, _gpuStdDev;
    private int _cpuMax, _gpuMax;
    private bool _snapshotStale = true;

    private readonly Meter _meter;
    private readonly MeterProvider _meterProvider;

    public TelemetrySender()
    {
        _meter = new Meter("HardwareMonitor", "1.0.0");
        _meter.CreateObservableGauge("cpu.temperature.avg", () => { EnsureSnapshot(); return _cpuAvg; }, "C");
        _meter.CreateObservableGauge("cpu.temperature.max", () => { EnsureSnapshot(); return _cpuMax; }, "C");
        _meter.CreateObservableGauge("cpu.temperature.stddev", () => { EnsureSnapshot(); return _cpuStdDev; }, "C");
        _meter.CreateObservableGauge("gpu.temperature.avg", () => { EnsureSnapshot(); return _gpuAvg; }, "C");
        _meter.CreateObservableGauge("gpu.temperature.max", () => { EnsureSnapshot(); return _gpuMax; }, "C");
        _meter.CreateObservableGauge("gpu.temperature.stddev", () => { EnsureSnapshot(); return _gpuStdDev; }, "C");

        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(r => r.AddService("hardware-monitor"))
            .AddMeter("HardwareMonitor")
            .AddOtlpExporter((exporterOptions, readerOptions) =>
            {
                exporterOptions.Endpoint = new Uri("http://localhost:4318/v1/metrics");
                exporterOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 60_000;
            })
            .Build()!;
    }

    public void RecordTemperatures(int cpuTemp, int gpuTemp)
    {
        lock (_lock)
        {
            _cpuSamples.Add(cpuTemp);
            _gpuSamples.Add(gpuTemp);
            _snapshotStale = true;
        }
    }

    private void EnsureSnapshot()
    {
        lock (_lock)
        {
            if (!_snapshotStale) return;

            _cpuAvg = _cpuSamples.Count > 0 ? _cpuSamples.Average() : 0;
            _cpuMax = _cpuSamples.Count > 0 ? _cpuSamples.Max() : 0;
            _cpuStdDev = StdDev(_cpuSamples);

            _gpuAvg = _gpuSamples.Count > 0 ? _gpuSamples.Average() : 0;
            _gpuMax = _gpuSamples.Count > 0 ? _gpuSamples.Max() : 0;
            _gpuStdDev = StdDev(_gpuSamples);

            _cpuSamples.Clear();
            _gpuSamples.Clear();
            _snapshotStale = false;
        }
    }

    private static double StdDev(List<int> samples)
    {
        if (samples.Count < 2) return 0;
        double avg = samples.Average();
        double sumSquares = samples.Sum(s => (s - avg) * (s - avg));
        return Math.Sqrt(sumSquares / samples.Count);
    }

    public void Dispose()
    {
        _meterProvider.Dispose();
        _meter.Dispose();
    }
}
