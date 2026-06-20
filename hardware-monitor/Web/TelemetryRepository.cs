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
    // Known metric names. The schema is generic, but only these two are populated for now.
    public const string CpuMetric = "cpu_temp";
    public const string GpuMetric = "gpu_temp";

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
            """;
        command.ExecuteNonQuery();
        _logger.LogInformation("SQLite store initialized (WAL) at {ConnectionString}", _connectionString);
    }

    /// <summary>
    /// Persists one CPU and one GPU reading at the given timestamp. Failures are logged and swallowed.
    /// </summary>
    public void InsertReadings(long timestampUtcMs, double cpuValue, double gpuValue)
    {
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

            metricParam.Value = CpuMetric;
            valueParam.Value = cpuValue;
            command.ExecuteNonQuery();

            metricParam.Value = GpuMetric;
            valueParam.Value = gpuValue;
            command.ExecuteNonQuery();

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

    /// <summary>
    /// Dev helper: seeds synthetic CPU/GPU readings at one-minute spacing over the past
    /// <paramref name="hours"/> hours so the 24h chart can be demonstrated without waiting.
    /// Returns the number of rows inserted.
    /// </summary>
    public int SeedSynthetic(int hours)
    {
        int inserted = 0;
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
                // Gentle sine-wave "gaming sessions" plus noise, distinct CPU/GPU baselines.
                double phase = (ts - startMs) / 3_600_000.0; // hours elapsed
                double cpu = 45 + 20 * Math.Max(0, Math.Sin(phase)) + random.NextDouble() * 4;
                double gpu = 50 + 25 * Math.Max(0, Math.Sin(phase + 0.5)) + random.NextDouble() * 4;

                tsParam.Value = ts;
                metricParam.Value = CpuMetric;
                valueParam.Value = Math.Round(cpu, 1);
                command.ExecuteNonQuery();

                metricParam.Value = GpuMetric;
                valueParam.Value = Math.Round(gpu, 1);
                command.ExecuteNonQuery();
                inserted += 2;
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
}
