using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace hardware_monitor.Web;

/// <summary>
/// Minimal-API endpoints exposing the stored telemetry as JSON for the local dashboard.
/// </summary>
public static class TelemetryApi
{
    private static readonly HashSet<string> ValidMetrics = new()
    {
        TelemetryRepository.CpuMetric,
        TelemetryRepository.GpuMetric
    };

    public static void MapTelemetryApi(this WebApplication app)
    {
        // GET /api/telemetry/history?metric=cpu_temp&window=24h
        app.MapGet("/api/telemetry/history", (string? metric, string? window, TelemetryRepository repo) =>
        {
            if (metric is null || !ValidMetrics.Contains(metric))
            {
                return Results.BadRequest(new { error = "Unknown or missing metric. Use 'cpu_temp' or 'gpu_temp'." });
            }

            if (!TryResolveWindow(window, out long durationMs, out long bucketMs))
            {
                return Results.BadRequest(new { error = "Unknown or missing window. Use '1h', '24h' or '7d'." });
            }

            long since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - durationMs;
            List<DataPoint> points = repo.GetHistory(metric, since, bucketMs);
            return Results.Json(points);
        });

        // GET /api/telemetry/current  -> latest CPU & GPU temperature for the live readout
        app.MapGet("/api/telemetry/current", (LatestReadings latest) =>
        {
            var (cpu, gpu, ts) = latest.Snapshot();
            return Results.Json(new { cpu, gpu, t = ts });
        });

        // POST /api/dev/seed?hours=24  -> dev helper to populate synthetic history for demos
        app.MapPost("/api/dev/seed", (int? hours, TelemetryRepository repo) =>
        {
            int h = hours is > 0 and <= 168 ? hours.Value : 24; // cap at 7 days
            int inserted = repo.SeedSynthetic(h);
            return Results.Json(new { inserted, hours = h });
        });
    }

    /// <summary>
    /// Maps a window token to its duration and downsample bucket size (0 = return raw rows).
    /// </summary>
    private static bool TryResolveWindow(string? window, out long durationMs, out long bucketMs)
    {
        switch (window)
        {
            case "1h":
                durationMs = 3_600_000L;        // 1 hour
                bucketMs = 0;                    // raw (~720 points at 5s cadence)
                return true;
            case "24h":
                durationMs = 86_400_000L;        // 24 hours
                bucketMs = 60_000;               // per-minute average (~1440 points)
                return true;
            case "7d":
                durationMs = 604_800_000L;       // 7 days
                bucketMs = 600_000;              // per-10-minute average (~1008 points)
                return true;
            default:
                durationMs = 0;
                bucketMs = 0;
                return false;
        }
    }
}
