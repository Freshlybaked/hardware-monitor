using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace hardware_monitor.Web;

/// <summary>
/// Minimal-API endpoints exposing the stored telemetry as JSON for the local dashboard.
/// </summary>
public static class TelemetryApi
{
    public static void MapTelemetryApi(this WebApplication app)
    {
        // GET /api/telemetry/metrics -> the catalog of groups/series the dashboard should render
        app.MapGet("/api/telemetry/metrics", (MetricCatalog catalog) => Results.Json(catalog.Groups));

        // GET /api/telemetry/history?metric=cpu_temp&window=24h
        app.MapGet("/api/telemetry/history", (string? metric, string? window, MetricCatalog catalog, TelemetryRepository repo) =>
        {
            if (metric is null || !catalog.ValidMetrics.Contains(metric))
            {
                return Results.BadRequest(new { error = $"Unknown or missing metric. Valid: {string.Join(", ", catalog.ValidMetrics)}." });
            }

            if (!TryResolveWindow(window, out long durationMs, out long bucketMs))
            {
                return Results.BadRequest(new { error = "Unknown or missing window. Use '1h', '24h' or '7d'." });
            }

            long since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - durationMs;
            List<DataPoint> points = repo.GetHistory(metric, since, bucketMs);
            return Results.Json(points);
        });

        // GET /api/telemetry/current  -> latest value of every metric for the live readout
        app.MapGet("/api/telemetry/current", (LatestReadings latest) =>
        {
            var (values, ts) = latest.Snapshot();
            return Results.Json(new { t = ts, values });
        });

        // POST /api/dev/seed?hours=24  -> dev helper to populate synthetic history for demos
        app.MapPost("/api/dev/seed", (int? hours, MetricCatalog catalog, TelemetryRepository repo) =>
        {
            int h = hours is > 0 and <= 168 ? hours.Value : 24; // cap at 7 days
            int inserted = repo.SeedSynthetic(h, catalog.ValidMetrics);
            return Results.Json(new { inserted, hours = h });
        });

        // GET /api/config/thresholds  -> the currently saved CPU/GPU warn+crit temperature limits
        app.MapGet("/api/config/thresholds", (TelemetryRepository repo, IOptions<MonitorOptions> options) =>
        {
            var defaults = DefaultThresholds(options.Value);
            var t = repo.GetThresholds(defaults);
            return Results.Json(new { cpu_warn = t.CpuWarn, cpu_crit = t.CpuCrit, gpu_warn = t.GpuWarn, gpu_crit = t.GpuCrit });
        });

        // POST /api/config/thresholds  -> validate, persist, and push the new limits to the display
        app.MapPost("/api/config/thresholds", async (ThresholdUpdate body, TelemetryRepository repo, DisplaySender display) =>
        {
            if (!IsValidThreshold(body.CpuWarn) || !IsValidThreshold(body.CpuCrit) ||
                !IsValidThreshold(body.GpuWarn) || !IsValidThreshold(body.GpuCrit))
            {
                return Results.BadRequest(new { error = "All thresholds must be numbers between 30 and 120 (°C)." });
            }
            if (body.CpuWarn >= body.CpuCrit || body.GpuWarn >= body.GpuCrit)
            {
                return Results.BadRequest(new { error = "Each warning threshold must be below its critical threshold." });
            }

            var thresholds = new Thresholds(body.CpuWarn, body.CpuCrit, body.GpuWarn, body.GpuCrit);
            repo.SaveThresholds(thresholds);
            bool sent = await display.SendAsync(thresholds.ToConfigPayload());
            return Results.Json(new
            {
                cpu_warn = thresholds.CpuWarn,
                cpu_crit = thresholds.CpuCrit,
                gpu_warn = thresholds.GpuWarn,
                gpu_crit = thresholds.GpuCrit,
                sent
            });
        });
    }

    /// <summary>The configured fallback thresholds, used until the user saves their own.</summary>
    private static Thresholds DefaultThresholds(MonitorOptions o) =>
        new(o.CpuWarnThreshold, o.CpuCritThreshold, o.GpuWarnThreshold, o.GpuCritThreshold);

    /// <summary>Body of POST /api/config/thresholds (JSON, e.g. {"cpu_warn":75,"cpu_crit":90,"gpu_warn":70,"gpu_crit":85}).</summary>
    public record ThresholdUpdate(
        [property: JsonPropertyName("cpu_warn")] double CpuWarn,
        [property: JsonPropertyName("cpu_crit")] double CpuCrit,
        [property: JsonPropertyName("gpu_warn")] double GpuWarn,
        [property: JsonPropertyName("gpu_crit")] double GpuCrit);

    /// <summary>A threshold must be a real, finite temperature within a sane range.</summary>
    private static bool IsValidThreshold(double value) =>
        double.IsFinite(value) && value >= 30 && value <= 120;

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
