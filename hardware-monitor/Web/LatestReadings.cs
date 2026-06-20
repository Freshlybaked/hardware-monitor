namespace hardware_monitor.Web;

/// <summary>
/// Thread-safe holder for the most recent CPU/GPU temperatures. The sampling loop writes here
/// every second; the /api/telemetry/current endpoint reads from it for a live readout, avoiding
/// a database hit on every poll.
/// </summary>
public class LatestReadings
{
    private readonly object _lock = new();
    private int _cpu;
    private int _gpu;
    private long _timestampUtcMs;

    public void Update(int cpu, int gpu)
    {
        lock (_lock)
        {
            _cpu = cpu;
            _gpu = gpu;
            _timestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public (int Cpu, int Gpu, long TimestampUtcMs) Snapshot()
    {
        lock (_lock)
        {
            return (_cpu, _gpu, _timestampUtcMs);
        }
    }
}
