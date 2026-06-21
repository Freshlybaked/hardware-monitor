namespace hardware_monitor.Web;

/// <summary>
/// Thread-safe holder for the most recent value of every metric. The sampling loop writes here each
/// second; the /api/telemetry/current endpoint and the persistence service read snapshots from it,
/// avoiding a database hit for the live readout.
/// </summary>
public class LatestReadings
{
    private readonly object _lock = new();
    private Dictionary<string, double> _values = new();
    private long _timestampUtcMs;

    public void Update(IReadOnlyDictionary<string, double> values)
    {
        lock (_lock)
        {
            _values = new Dictionary<string, double>(values);
            _timestampUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public (IReadOnlyDictionary<string, double> Values, long TimestampUtcMs) Snapshot()
    {
        lock (_lock)
        {
            return (new Dictionary<string, double>(_values), _timestampUtcMs);
        }
    }
}
