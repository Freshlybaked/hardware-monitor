using hardware_monitor.Web;

public class DebugSensorRetriever : ISensorRetriever
{
    private const int BaselineTemp = 40;
    private const int MaxTemp = 90;
    private const double ChanceToStartGaming = 0.05;
    private const double ChanceToStopGaming = 0.03;

    private readonly Random _random = new();
    private bool _gaming;
    private double _cpuTemp = BaselineTemp;
    private double _gpuTemp = BaselineTemp;

    private double _ram = 45;
    private readonly Dictionary<string, double> _diskPct = new();

    public DebugSensorRetriever(IReadOnlyList<DriveInfo>? drivesToTrack = null)
    {
        var drives = (drivesToTrack is { Count: > 0 })
            ? new List<DriveInfo>(drivesToTrack)
            : StorageDrives.Resolve(null);

        foreach (DriveInfo drive in drives)
        {
            // Seed each tracked drive with a plausible, stable-ish fill level.
            _diskPct[StorageDrives.Letter(drive)] = 50 + _random.NextDouble() * 35;
        }
    }

    public void Init()
    {
        Console.WriteLine("[Debug] Using simulated sensor data");
    }

    public int GetCPUTemp()
    {
        UpdateState();
        return (int)_cpuTemp;
    }

    public int GetGPUTemp()
    {
        return (int)_gpuTemp;
    }

    public double GetRamUsedPercent()
    {
        // Gentle random walk; climbs while "gaming".
        _ram += (_gaming ? _random.NextDouble() * 2 : -_random.NextDouble() * 1.5);
        _ram = Math.Clamp(_ram, 25, 95);
        return Math.Round(_ram, 1);
    }

    public (double DownMbps, double UpMbps) GetNetworkThroughput()
    {
        // Occasional download spikes; small upload.
        double down = _random.NextDouble() < 0.3 ? _random.NextDouble() * 200 : _random.NextDouble() * 10;
        double up = _random.NextDouble() * 5;
        return (Math.Round(down, 2), Math.Round(up, 2));
    }

    public IReadOnlyList<DriveUsage> GetStorageUsage()
    {
        var result = new List<DriveUsage>(_diskPct.Count);
        foreach (var letter in _diskPct.Keys.ToList())
        {
            // Drift very slowly so the chart isn't a flat line.
            double next = Math.Clamp(_diskPct[letter] + (_random.NextDouble() - 0.5) * 0.1, 10, 99);
            _diskPct[letter] = next;
            result.Add(new DriveUsage(letter, Math.Round(next, 1)));
        }
        return result;
    }

    private void UpdateState()
    {
        if (_gaming && _random.NextDouble() < ChanceToStopGaming)
            _gaming = false;
        else if (!_gaming && _random.NextDouble() < ChanceToStartGaming)
            _gaming = true;

        if (_gaming)
        {
            _cpuTemp += _random.NextDouble() * 3;
            _gpuTemp += _random.NextDouble() * 4;
        }
        else
        {
            _cpuTemp -= _random.NextDouble() * 2;
            _gpuTemp -= _random.NextDouble() * 2.5;
        }

        _cpuTemp = Math.Clamp(_cpuTemp, BaselineTemp, MaxTemp);
        _gpuTemp = Math.Clamp(_gpuTemp, BaselineTemp, MaxTemp);
    }
}
