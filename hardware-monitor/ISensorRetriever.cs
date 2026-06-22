public interface ISensorRetriever
{
    void Init();
    int GetCPUTemp();
    int GetGPUTemp();

    /// <summary>Physical RAM in use, as a percentage (0-100).</summary>
    double GetRamUsedPercent();

    /// <summary>Current network throughput on the active adapter, in megabits per second.</summary>
    (double DownMbps, double UpMbps) GetNetworkThroughput();

    /// <summary>Used capacity per tracked drive, as a percentage (0-100).</summary>
    IReadOnlyList<DriveUsage> GetStorageUsage();
}

/// <summary>Capacity usage for a single drive. <see cref="Letter"/> is the bare drive letter, e.g. "C".</summary>
public readonly record struct DriveUsage(string Letter, double UsedPercent);
