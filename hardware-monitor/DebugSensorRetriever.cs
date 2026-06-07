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
