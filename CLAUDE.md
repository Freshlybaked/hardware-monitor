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

Temperature payload sent to the ESP32 display is `"CC:GG"` where CC and GG are zero-padded two-digit integers (e.g., `"53:48"`).

## Key Dependencies

- **LibreHardwareMonitorLib** (pre-release) — hardware sensor access, requires PawnIO kernel driver
- **OpenTelemetry.Exporter.OpenTelemetryProtocol** — OTLP metric export
- **Zeroconf** — mDNS discovery for ESP32 UDP target
