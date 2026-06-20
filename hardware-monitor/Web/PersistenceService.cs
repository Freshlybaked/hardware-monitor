using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace hardware_monitor.Web;

/// <summary>
/// Persists the latest CPU/GPU readings to SQLite on a coarse cadence (default every 5s) so the
/// database stays small while still capturing trends — independent of the 1 Hz live display. Also
/// prunes rows older than the retention window on startup and once per day to bound disk growth.
/// </summary>
public class PersistenceService : BackgroundService
{
    private readonly TelemetryRepository _repository;
    private readonly LatestReadings _latest;
    private readonly MonitorOptions _options;
    private readonly ILogger<PersistenceService> _logger;

    public PersistenceService(
        TelemetryRepository repository,
        LatestReadings latest,
        IOptions<MonitorOptions> options,
        ILogger<PersistenceService> logger)
    {
        _repository = repository;
        _latest = latest;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int intervalSeconds = Math.Max(1, _options.PersistIntervalSeconds);
        _logger.LogInformation(
            "Persistence started: writing every {Interval}s, retaining {Days} days",
            intervalSeconds, _options.RetentionDays);

        // Prune once at startup, then track when the next daily prune is due.
        PruneOldRows();
        DateTime nextPruneUtc = DateTime.UtcNow.AddDays(1);

        var period = TimeSpan.FromSeconds(intervalSeconds);
        using var timer = new PeriodicTimer(period);

        while (await SafeWaitForNextTick(timer, stoppingToken))
        {
            var (cpu, gpu, ts) = _latest.Snapshot();
            if (ts == 0)
            {
                // No reading has been taken yet (sampling loop just started); skip this tick.
                continue;
            }

            _repository.InsertReadings(ts, cpu, gpu);

            if (DateTime.UtcNow >= nextPruneUtc)
            {
                PruneOldRows();
                nextPruneUtc = DateTime.UtcNow.AddDays(1);
            }
        }

        _logger.LogInformation("Persistence stopped");
    }

    private void PruneOldRows()
    {
        int retentionDays = Math.Max(1, _options.RetentionDays);
        long cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeMilliseconds();
        _repository.Prune(cutoff);
    }

    private static async Task<bool> SafeWaitForNextTick(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
