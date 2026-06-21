# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Run

```powershell
# Build
dotnet build hardware-monitor/hardware-monitor.csproj

# Run (requires administrator privileges due to app.manifest)
dotnet run --project hardware-monitor/hardware-monitor.csproj
```

The app requires **PawnIO** to be installed (kernel driver for hardware sensor access). It will prompt and exit if PawnIO is missing.

There are no tests in this project.

## What This Application Does

A Windows desktop console app that reads CPU and GPU temperatures every second while gaming and sends them to a physical ESP32 display and to Grafana Cloud for visualization. It is **not** containerized — it runs directly on the user's personal gaming PC.

## Architecture

**Program.cs** — Top-level entry point (no Main method, uses top-level statements). Orchestrates the polling loop: reads temps from `SensorRetriever`, sends a formatted payload to the display via `SerialWriter` or `UdpSender`, and records metrics via `TelemetrySender`. Serial is preferred over UDP when both are available.

**SensorRetriever** — Wraps LibreHardwareMonitorLib to access CPU/GPU hardware sensors. Opens a `Computer` instance with only CPU and GPU enabled. If multiple GPUs are detected, prompts the user to choose one interactively.

**SerialWriter** — Sends temperature payloads over a serial COM port (115200 baud) to an ESP32 display device. Handles multi-port selection.

**UdpSender** — Alternative to serial: discovers the ESP32 display via mDNS/Zeroconf (`_esp32udp._udp.local.`) and sends payloads over UDP.

**TelemetrySender** — Exports CPU/GPU temperature metrics to Grafana Cloud via OpenTelemetry. Uses `ObservableGauge` instruments under a `HardwareMonitor` meter, exported via OTLP HTTP (protobuf) to a local Grafana Alloy instance on `localhost:4318`. Metrics export every 10 seconds. Metrics appear in Grafana Cloud Prometheus as `cpu_temperature` and `gpu_temperature`.

## Telemetry Pipeline

```
App (OTel SDK) → Grafana Alloy (localhost:4318/v1/metrics) → Grafana Cloud
```

- Alloy runs as a Windows service; its config is at `C:\Program Files\GrafanaLabs\Alloy\config.alloy`
- The `GCLOUD_RW_API_KEY` system environment variable holds the Grafana Cloud API token
- The OTLP endpoint must include the full signal path (`/v1/metrics`) — the OTel .NET SDK does not auto-append it in this configuration

## Payload Format

The payload sent to the ESP32 display (over serial and UDP) is a **versioned JSON object, one per line** — self-describing so the firmware reads only the keys it renders and ignores the rest:

```json
{"v":1,"cpu":53,"gpu":48,"ram":41,"net":{"dn":12.3,"up":0.4},"disk":{"C":38,"D":72.4}}
```

- `v` — schema version (bump on any breaking change)
- `cpu`, `gpu` — integers, °C
- `ram` — number, % used
- `net.dn`, `net.up` — numbers, Mbps (active adapter)
- `disk` — object keyed by drive letter → % used; one key per tracked drive (see `Drives` in appsettings), so the count varies

Built in `SamplingService.CreatePayload` via `System.Text.Json`. The string carries no trailing newline — `SerialWriter` appends `"\n"`, and UDP frames per datagram. (Previously the format was `"CC:GG"`, two zero-padded integers; replaced because it couldn't represent the added metrics or a variable number of drives.)

## Key Dependencies

- **LibreHardwareMonitorLib** (pre-release) — hardware sensor access, requires PawnIO kernel driver
- **OpenTelemetry.Exporter.OpenTelemetryProtocol** — OTLP metric export
- **Zeroconf** — mDNS discovery for ESP32 UDP target
