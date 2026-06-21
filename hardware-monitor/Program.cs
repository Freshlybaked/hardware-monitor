using System.Diagnostics;
using hardware_monitor.Web;
using LibreHardwareMonitor.PawnIo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// This application requires PawnIO to be installed
if (!PawnIo.IsInstalled)
{
    Console.WriteLine("PawnIO not installed. This console application requires PawnIO to be installed to run.");
    Console.WriteLine("Press enter to open the PawnIO website in your browser and terminate this application.");
    Console.ReadLine();
    String url = "https://pawnio.eu/";
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error opening browser: {ex.Message}");
    }
    return;
}

// ---------------------------------------------------------------------------
// Configuration first, so the sensor retriever and metric catalog can be built
// from the configured drive list before the interactive startup runs.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection(MonitorOptions.SectionName));
var options = builder.Configuration.GetSection(MonitorOptions.SectionName).Get<MonitorOptions>() ?? new MonitorOptions();

// Resolve the drives we'll track (defaults to the system drive) and build the metric catalog.
var drives = StorageDrives.Resolve(options.Drives);
var catalog = new MetricCatalog(drives.Select(StorageDrives.Letter));

// ---------------------------------------------------------------------------
// Interactive startup (unchanged): pick mode, sensors, display transport.
// These prompts must run on the console before the web host takes over.
// ---------------------------------------------------------------------------

bool debugMode = doEnableDebugMode();
ISensorRetriever sensorRetriever;

if (debugMode)
{
    sensorRetriever = new DebugSensorRetriever(drives);
}
else
{
    sensorRetriever = new SensorRetriever(drives);
}

// Init sensor retriever which retrieves CPU and GPU instances
sensorRetriever.Init();

// try to retrieve serial port
SerialWriter serialWriter = new();
bool serialAvailable = serialWriter.TryOpenPort();
Console.WriteLine($"Is Serial Available: {serialAvailable}");

// init telemetry sender for Grafana Cloud via Alloy
// TelemetrySender telemetrySender = new();
// Console.WriteLine("Telemetry sender initialized (sending to Alloy on localhost:4318)");

// try to retrieve udp port
UdpSender udpSender = new("_esp32udp._udp.local.", "sensordisplay", "_esp32udp");
bool udpAvailable = await udpSender.FindEsp32Ip();
Console.WriteLine($"Is UDP Available: {udpAvailable}");

// ---------------------------------------------------------------------------
// Build the single in-process host: sampling loop + persistence + web API.
// ---------------------------------------------------------------------------

// Bind Kestrel to localhost only (the configured URL uses 'localhost').
builder.WebHost.UseUrls(options.WebUrl);

// Share the already-initialized objects with the hosted services / API.
builder.Services.AddSingleton(sensorRetriever);
builder.Services.AddSingleton(serialWriter);
builder.Services.AddSingleton(udpSender);
builder.Services.AddSingleton(catalog);
// builder.Services.AddSingleton(telemetrySender);
builder.Services.AddSingleton<LatestReadings>();

// Single send path to the display (serial preferred, else UDP), shared by the sampling loop and
// the config endpoint. Carries the startup-determined transport availability flags.
builder.Services.AddSingleton(sp => new DisplaySender(
    serialWriter, serialAvailable,
    udpSender, udpAvailable,
    sp.GetRequiredService<ILogger<DisplaySender>>()));

// SQLite repository (logger injected by the container).
builder.Services.AddSingleton(sp => new TelemetryRepository(
    options.DatabasePath,
    sp.GetRequiredService<ILogger<TelemetryRepository>>()));

// Sampling loop sends via the shared DisplaySender.
builder.Services.AddHostedService(sp => new SamplingService(
    sp.GetRequiredService<ISensorRetriever>(),
    sp.GetRequiredService<DisplaySender>(),
    // sp.GetRequiredService<TelemetrySender>(),
    sp.GetRequiredService<LatestReadings>(),
    sp.GetRequiredService<ILogger<SamplingService>>()));

builder.Services.AddHostedService<PersistenceService>();

var app = builder.Build();

// Create schema + enable WAL before anything reads/writes.
var repository = app.Services.GetRequiredService<TelemetryRepository>();
repository.Initialize();

// Push the current alert thresholds to the display once at startup so a freshly powered display
// knows the limits immediately (falls back to the configured defaults until the user saves).
var display = app.Services.GetRequiredService<DisplaySender>();
var startupThresholds = repository.GetThresholds(new Thresholds(
    options.CpuWarnThreshold, options.CpuCritThreshold,
    options.GpuWarnThreshold, options.GpuCritThreshold));
await display.SendAsync(startupThresholds.ToConfigPayload());
Console.WriteLine($"Sent startup thresholds: {startupThresholds.ToConfigPayload()}");

// Serve the dashboard (wwwroot/index.html) and the JSON API.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapTelemetryApi();

Console.WriteLine($"Web dashboard available at {options.WebUrl}");

try
{
    app.Run();
}
finally
{
    // Flush the final OpenTelemetry export on shutdown.
    // telemetrySender.Dispose();
}

static bool doEnableDebugMode()
{
    string debugMode = "";
    while (debugMode == "")
    {
        Console.WriteLine("Do you want to run in debug mode? 'y' for debug mode, 'n' for normal mode.");
        string? input = Console.ReadLine();
        if (input != null)
        {
            try
            {
                if (input.ToLower().StartsWith("y"))
                {
                    Console.WriteLine("Debug mode enabled");
                    debugMode = "y";
                }
                else
                {
                    Console.WriteLine("Normal mode enabled");
                    debugMode = "n";
                }
            }
            catch (Exception)
            {
                continue;
            }
        }
    }

    return debugMode == "y";
}
