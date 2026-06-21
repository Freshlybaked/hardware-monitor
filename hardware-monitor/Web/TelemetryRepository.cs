using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace hardware_monitor.Web;

/// <summary>
/// A data point returned to the dashboard: epoch-millisecond timestamp + value.
/// </summary>
public readonly record struct DataPoint(long T, double V);

/// <summary>
/// Thin repository over a local SQLite database holding temperature readings in narrow/long form.
/// All writes are wrapped in try/catch so a storage failure can never stall the sampling/serial loop.
/// </summary>
public class TelemetryRepository
{
    private readonly string _connectionString;
    private readonly ILogger<TelemetryRepository> _logger;

    public TelemetryRepository(string databasePath, ILogger<TelemetryRepository> logger)
    {
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // Wait rather than fail immediately if the writer/reader briefly contend for the file.
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// Creates the schema (if needed) and enables WAL so the background writer and the web reader
    /// don't block each other. Throws on failure — initial schema creation is a hard prerequisite.
    /// </summary>
    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Readings(
                Id           INTEGER PRIMARY KEY,
                TimestampUtc INTEGER NOT NULL,
                Metric       TEXT    NOT NULL,
                Value        REAL    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_readings_metric_time
                ON Readings(Metric, TimestampUtc);
            CREATE TABLE IF NOT EXISTS Settings(
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        _logger.LogInformation("SQLite store initialized (WAL) at {ConnectionString}", _connectionString);
    }

    /// <summary>
    /// Persists one row per supplied metric at the given timestamp. Failures are logged and swallowed.
    /// </summary>
    public void InsertReadings(long timestampUtcMs, IReadOnlyDictionary<string, double> values)
    {
        if (values.Count == 0) return;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Readings (TimestampUtc, Metric, Value)
                VALUES ($ts, $metric, $value);
                """;
            var tsParam = command.Parameters.Add("$ts", SqliteType.Integer);
            var metricParam = command.Parameters.Add("$metric", SqliteType.Text);
            var valueParam = command.Parameters.Add("$value", SqliteType.Real);

            tsParam.Value = timestampUtcMs;
            foreach (var (metric, value) in values)
            {
                metricParam.Value = metric;
                valueParam.Value = value;
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            // Reliability requirement: a failed write must never crash or stall the sampling loop.
            _logger.LogError(ex, "Failed to persist readings to SQLite");
        }
    }

    /// <summary>
    /// Returns the history for a metric over the given window. When bucketMs &gt; 0 the rows are
    /// averaged into time buckets server-side (e.g. per-minute for 24h) to keep the payload small.
    /// </summary>
    public List<DataPoint> GetHistory(string metric, long sinceUtcMs, long bucketMs)
    {
        var points = new List<DataPoint>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            if (bucketMs > 0)
            {
                command.CommandText = """
                    SELECT (TimestampUtc / $bucket) * $bucket AS Bucket, AVG(Value) AS Avg
                    FROM Readings
                    WHERE Metric = $metric AND TimestampUtc >= $since
                    GROUP BY Bucket
                    ORDER BY Bucket;
                    """;
                command.Parameters.AddWithValue("$bucket", bucketMs);
            }
            else
            {
                command.CommandText = """
                    SELECT TimestampUtc, Value
                    FROM Readings
                    WHERE Metric = $metric AND TimestampUtc >= $since
                    ORDER BY TimestampUtc;
                    """;
            }

            command.Parameters.AddWithValue("$metric", metric);
            command.Parameters.AddWithValue("$since", sinceUtcMs);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                points.Add(new DataPoint(reader.GetInt64(0), Math.Round(reader.GetDouble(1), 2)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query history for metric {Metric}", metric);
        }
        return points;
    }

    /// <summary>
    /// Deletes rows older than the cutoff. Returns the number of rows removed (0 on failure).
    /// </summary>
    public int Prune(long cutoffUtcMs)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Readings WHERE TimestampUtc < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoffUtcMs);
            int removed = command.ExecuteNonQuery();
            if (removed > 0)
            {
                _logger.LogInformation("Pruned {Count} readings older than {Cutoff}", removed, cutoffUtcMs);
            }
            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prune old readings");
            return 0;
        }
    }

    private const string CpuWarnKey = "threshold_cpu_warn";
    private const string CpuCritKey = "threshold_cpu_crit";
    private const string GpuWarnKey = "threshold_gpu_warn";
    private const string GpuCritKey = "threshold_gpu_crit";

    /// <summary>
    /// Reads the saved alert thresholds, falling back to <paramref name="defaults"/> for any value
    /// that has never been saved (or if the read fails).
    /// </summary>
    public Thresholds GetThresholds(Thresholds defaults)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT Key, Value FROM Settings WHERE Key IN ($cw, $cc, $gw, $gc);";
            command.Parameters.AddWithValue("$cw", CpuWarnKey);
            command.Parameters.AddWithValue("$cc", CpuCritKey);
            command.Parameters.AddWithValue("$gw", GpuWarnKey);
            command.Parameters.AddWithValue("$gc", GpuCritKey);

            double cpuWarn = defaults.CpuWarn, cpuCrit = defaults.CpuCrit;
            double gpuWarn = defaults.GpuWarn, gpuCrit = defaults.GpuCrit;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                if (double.TryParse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    switch (key)
                    {
                        case CpuWarnKey: cpuWarn = value; break;
                        case CpuCritKey: cpuCrit = value; break;
                        case GpuWarnKey: gpuWarn = value; break;
                        case GpuCritKey: gpuCrit = value; break;
                    }
                }
            }
            return new Thresholds(cpuWarn, cpuCrit, gpuWarn, gpuCrit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read thresholds; using defaults");
            return defaults;
        }
    }

    /// <summary>Upserts the alert thresholds. Failures are logged and swallowed.</summary>
    public void SaveThresholds(Thresholds thresholds)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Settings (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            var keyParam = command.Parameters.Add("$key", SqliteType.Text);
            var valueParam = command.Parameters.Add("$value", SqliteType.Text);

            void Upsert(string key, double value)
            {
                keyParam.Value = key;
                valueParam.Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                command.ExecuteNonQuery();
            }

            Upsert(CpuWarnKey, thresholds.CpuWarn);
            Upsert(CpuCritKey, thresholds.CpuCrit);
            Upsert(GpuWarnKey, thresholds.GpuWarn);
            Upsert(GpuCritKey, thresholds.GpuCrit);

            transaction.Commit();
            _logger.LogInformation(
                "Saved thresholds cpu_warn={CpuWarn} cpu_crit={CpuCrit} gpu_warn={GpuWarn} gpu_crit={GpuCrit}",
                thresholds.CpuWarn, thresholds.CpuCrit, thresholds.GpuWarn, thresholds.GpuCrit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save thresholds");
        }
    }

    private const string TrackedDrivesKey = "tracked_drives";

    /// <summary>
    /// Reads the saved tracked-drive selection (a JSON array of bare letters, e.g. ["C","D"]).
    /// Returns <c>null</c> if the user has never saved a selection or if the read/parse fails, so the
    /// caller can fall back to appsettings.
    /// </summary>
    public List<string>? GetTrackedDrives()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
            command.Parameters.AddWithValue("$key", TrackedDrivesKey);

            if (command.ExecuteScalar() is string json)
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read tracked drives; falling back to configured defaults");
            return null;
        }
    }

    /// <summary>Upserts the tracked-drive selection as a JSON array. Failures are logged and swallowed.</summary>
    public void SaveTrackedDrives(IEnumerable<string> letters)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(letters.ToList());
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Settings (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            command.Parameters.AddWithValue("$key", TrackedDrivesKey);
            command.Parameters.AddWithValue("$value", json);
            command.ExecuteNonQuery();
            _logger.LogInformation("Saved tracked drives {Drives}", json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tracked drives");
        }
    }

    /// <summary>
    /// Dev helper: seeds synthetic readings for the supplied metrics at one-minute spacing over the
    /// past <paramref name="hours"/> hours so the charts can be demonstrated without waiting.
    /// Returns the number of rows inserted.
    /// </summary>
    public int SeedSynthetic(int hours, IReadOnlyCollection<string> metrics)
    {
        int inserted = 0;
        if (metrics.Count == 0) return 0;

        try
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long stepMs = 60_000; // one point per minute
            long startMs = nowMs - (long)hours * 3_600_000;
            var random = new Random();

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Readings (TimestampUtc, Metric, Value)
                VALUES ($ts, $metric, $value);
                """;
            var tsParam = command.Parameters.Add("$ts", SqliteType.Integer);
            var metricParam = command.Parameters.Add("$metric", SqliteType.Text);
            var valueParam = command.Parameters.Add("$value", SqliteType.Real);

            for (long ts = startMs; ts <= nowMs; ts += stepMs)
            {
                double phaseHours = (ts - startMs) / 3_600_000.0; // hours elapsed
                tsParam.Value = ts;
                foreach (string metric in metrics)
                {
                    metricParam.Value = metric;
                    valueParam.Value = SynthValue(metric, phaseHours, random);
                    command.ExecuteNonQuery();
                    inserted++;
                }
            }

            transaction.Commit();
            _logger.LogInformation("Seeded {Count} synthetic readings over {Hours}h", inserted, hours);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed synthetic readings");
        }
        return inserted;
    }

    /// <summary>Produces a plausible value for a metric based on its name (sine "sessions" + noise).</summary>
    private static double SynthValue(string metric, double phaseHours, Random rng)
    {
        if (metric.Contains("temp"))
            return Math.Round(45 + 20 * Math.Max(0, Math.Sin(phaseHours)) + rng.NextDouble() * 4, 1);
        if (metric.StartsWith("ram"))
            return Math.Round(Math.Clamp(40 + 30 * Math.Max(0, Math.Sin(phaseHours + 0.5)) + rng.NextDouble() * 5, 0, 100), 1);
        if (metric.StartsWith("net_down"))
            return Math.Round(rng.NextDouble() < 0.3 ? rng.NextDouble() * 200 : rng.NextDouble() * 8, 2);
        if (metric.StartsWith("net_up"))
            return Math.Round(rng.NextDouble() * 5, 2);
        if (metric.StartsWith("disk"))
            return Math.Round(Math.Clamp(60 + 5 * Math.Sin(phaseHours / 24.0) + rng.NextDouble() * 0.5, 0, 100), 1);
        return Math.Round(rng.NextDouble() * 100, 1);
    }
}
