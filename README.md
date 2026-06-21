# Hardware Monitor

A Windows desktop console appliance that reads CPU/GPU temperatures every second, drives an
external ESP32 display over serial/UDP, and (as of this change) **stores history locally and serves
an offline web dashboard from the same process**.

---

## Local history + web dashboard (new)

This feature adds local time-series storage and a self-hosted dashboard to the *existing* app. It
runs entirely in **one process**, fully **offline** — no cloud, no Grafana/Prometheus, no external
account, no internet.

### What was added

| Area | File(s) | Purpose |
|------|---------|---------|
| Storage | `Web/TelemetryRepository.cs` | `Microsoft.Data.Sqlite` repository. WAL mode, schema creation, insert, downsampled history queries, prune, synthetic seed. All writes wrapped in try/catch + logging so a DB failure can never stall sampling. |
| Live value bus | `Web/LatestReadings.cs` | Thread-safe holder of the latest CPU/GPU reading, written by the sampling loop, read by `/current`. |
| Sampling loop | `Web/SamplingService.cs` | `BackgroundService` holding the original 1 Hz loop (telemetry + serial/UDP send) — now also publishes each reading to `LatestReadings`. |
| Persistence + retention | `Web/PersistenceService.cs` | `BackgroundService` that writes a row per metric every 5 s (configurable) and prunes rows older than the retention window on startup + once/day. |
| JSON API | `Web/TelemetryApi.cs` | Minimal-API endpoints with query validation. |
| Config | `Web/MonitorOptions.cs`, `appsettings.json` | Strongly-typed settings. |
| Dashboard | `wwwroot/index.html`, `wwwroot/lib/chart.umd.min.js` | Dashboard page (with chart + live readout), Chart.js bundled locally (no CDN). |
| Config page | `wwwroot/config.html` | Second page for setting CPU/GPU temperature alert thresholds. |
| Thresholds | `Web/Thresholds.cs`, `Web/DisplaySender.cs` | Threshold record + `cfg` payload builder; single serial/UDP send path shared by the loop and the config endpoint. |
| Host wiring | `Program.cs` | Interactive startup unchanged; afterwards builds one ASP.NET Core host (Kestrel, localhost only) that runs the background services and serves the API + dashboard. |

The existing sensor/serial/UDP/telemetry classes (`SensorRetriever`, `SerialWriter`, `UdpSender`,
`TelemetrySender`) were **not modified** — the 1 Hz serial/display behaviour is unchanged.

### Configuration (`appsettings.json` → `HardwareMonitor`)

| Key | Default | Meaning |
|-----|---------|---------|
| `DatabasePath` | `hardware-monitor.db` | SQLite file (relative to working dir, or absolute). |
| `PersistIntervalSeconds` | `5` | How often a row per metric is written. Live display stays at 1 Hz. |
| `RetentionDays` | `7` | Rows older than this are pruned on startup and daily. |
| `WebUrl` | `http://localhost:5005` | Kestrel bind URL (localhost only). |

### Database schema

```sql
CREATE TABLE IF NOT EXISTS Readings(
    Id           INTEGER PRIMARY KEY,
    TimestampUtc INTEGER NOT NULL,   -- Unix epoch milliseconds, UTC
    Metric       TEXT    NOT NULL,   -- e.g. 'cpu_temp', 'gpu_temp', 'ram_used_pct', 'disk_used_pct_c'
    Value        REAL    NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_readings_metric_time ON Readings(Metric, TimestampUtc);

CREATE TABLE IF NOT EXISTS Settings(   -- key/value config (e.g. alert thresholds)
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
```

`Readings` is narrow/long form so more metrics can be added without a schema change. `Settings`
holds the saved CPU/GPU temperature alert thresholds (`threshold_cpu_temp`, `threshold_gpu_temp`).

### HTTP API

| Endpoint | Description |
|----------|-------------|
| `GET /api/telemetry/history?metric={cpu_temp\|gpu_temp}&window={1h\|24h\|7d}` | History as `[{ "t": <epoch ms>, "v": <value> }, ...]`. `1h` returns raw rows; `24h` is averaged **per minute** server-side (~1440 pts); `7d` is averaged per 10 minutes. Unknown metric/window → **400**. |
| `GET /api/telemetry/current` | Latest reading: `{ "cpu": <int>, "gpu": <int>, "t": <epoch ms> }`. |
| `POST /api/dev/seed?hours=24` | **Dev helper** — seeds synthetic per-minute history over the past N hours (1–168) so the 24h/7d charts can be demoed without waiting. Returns `{ "inserted": <n>, "hours": <n> }`. |
| `GET /api/config/thresholds` | Current CPU/GPU temperature alert limits: `{ "cpu": <°C>, "gpu": <°C> }`. |
| `POST /api/config/thresholds` | Body `{ "cpu": <°C>, "gpu": <°C> }`. Validates each is 30–120 °C (else **400**), saves to SQLite, and pushes a `cfg` line to the display. Returns `{ "cpu", "gpu", "sent" }` (`sent` = whether a display transport was available). |

### How to run

```powershell
# Build
dotnet build hardware-monitor/hardware-monitor.csproj

# Run (must be elevated — app.manifest requires administrator for PawnIO sensor access)
dotnet run --project hardware-monitor/hardware-monitor.csproj
```

Answer the startup prompts (`y` for debug/simulated sensors is handy for testing the dashboard with
no real hardware churn), then open:

**http://localhost:5005**

> Tip: debug mode (`DebugSensorRetriever`) produces simulated CPU/GPU temperatures, so you can verify
> the full pipeline — live readout, persistence, and chart — without gaming load.

### How to verify each requirement

1. **Samples at ~1 Hz, persists every ~5 s into a local `.db`:**
   - Watch the console — a `Payload: {"v":1,"t":"data","cpu":...}` JSON line prints every second.
   - A `hardware-monitor.db` file appears next to the working directory; alongside it `-wal`/`-shm`
     files confirm WAL mode is active.
   - Hit `GET /api/telemetry/current` repeatedly — `t` advances ~every second.
   - Let it run a minute, then `GET /api/telemetry/history?metric=cpu_temp&window=1h` — the number of
     points grows by ~12/min (one row per 5 s), confirming the coarser persist cadence.

2. **Working time-series chart with two windows + live readout (offline):**
   - Open http://localhost:5005. The CPU/GPU numeric cards update once per second.
   - The line chart shows two series (CPU + GPU) over time.
   - Click **Last 1 hour** / **Last 24 hours** / **Last 7 days** — the chart reloads for each window.
   - Disconnect from the internet first if you want to prove offline operation — Chart.js is served
     from `wwwroot/lib`, not a CDN.

3. **24h chart without waiting a day (seed helper):**
   ```powershell
   Invoke-RestMethod -Method Post "http://localhost:5005/api/dev/seed?hours=24"
   ```
   Then select **Last 24 hours** — the chart fills with a full day of synthetic data.

4. **Old rows pruned beyond retention:**
   - Set `RetentionDays` low (e.g. `1`) in `appsettings.json`, seed `hours=48`, restart the app.
   - On startup the pruner deletes rows older than the window (logged as
     `Pruned N readings older than ...`); the `7d` chart then shows only the retained tail.

5. **Alert thresholds (config page → display):**
   - From the dashboard, click **Configure alert thresholds**, set CPU/GPU limits, and **Save**.
   - The console emits a single `{"v":1,"t":"cfg","cpu":...,"gpu":...}` line on save (and once at
     startup). Reloading the config page shows the saved values (persisted in the `Settings` table).
   - On the dashboard with the **Temperature** metric selected, dashed CPU/GPU limit lines appear.

6. **Serial/display transport behaviour:**
   - With the ESP32 connected, the display still updates every second; serial is still preferred over
     UDP. The payload is now a versioned JSON line (see "Payload Format" in `CLAUDE.md`) carrying all
     metrics including per-drive disk usage — the ESP32 firmware must be updated to parse it.

### Validation examples (should return 400)

```text
GET /api/telemetry/history?metric=ram_temp&window=24h     -> 400 (unknown metric)
GET /api/telemetry/history?metric=cpu_temp&window=30m     -> 400 (unknown window)
```

### Note on the build warning

Restore emits `NU1903` for `SQLitePCLRaw.lib.e_sqlite3` (the bundled native SQLite). The advisory is
unpatched in the 2.1.x line that `Microsoft.Data.Sqlite.Core` 9.0.0 pins. For this appliance the
residual risk is negligible — it is local-only, every query is fully parameterized, and it never
reads untrusted SQL or untrusted database files. The newest available 2.1.x (2.1.11) is used.
