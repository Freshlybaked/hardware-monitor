using System.Diagnostics;
using LibreHardwareMonitor.PawnIo;

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

bool debugMode = doEnableDebugMode();
ISensorRetriever sensorRetriever;

if(debugMode)
{
    sensorRetriever = new DebugSensorRetriever();
}
else
{
    sensorRetriever = new SensorRetriever();
}

// Init sensor retriever which retrieves CPU and GPU instances
sensorRetriever.Init();

// try to retrieve serial port
SerialWriter serialWriter = new();
bool serialAvailable = serialWriter.TryOpenPort();
Console.WriteLine($"Is Serial Available: {serialAvailable}");

// init telemetry sender for Grafana Cloud via Alloy
TelemetrySender telemetrySender = new();
Console.WriteLine("Telemetry sender initialized (sending to Alloy on localhost:4318)");

// try to retrieve udp port
UdpSender udpSender = new("_esp32udp._udp.local.", "sensordisplay", "_esp32udp");
bool udpAvailable = await udpSender.FindEsp32Ip();
Console.WriteLine($"Is UDP Available: {udpAvailable}");

while (true)
{
    int cpuTemp = sensorRetriever.GetCPUTemp();
    int gpuTemp = sensorRetriever.GetGPUTemp();

    telemetrySender.RecordTemperatures(cpuTemp, gpuTemp);

    string payload = createPayload(cpuTemp, gpuTemp);
    Console.WriteLine($"Payload: {payload}");

    // we prioritise sending over serial port
    if (serialAvailable)
    {
        Console.WriteLine($"Sending payload {payload} over serial");
        serialWriter.SendMessage(payload);
    }
    else if(udpAvailable)
    {
        Console.WriteLine($"Sending payload {payload} over udp");
        await udpSender.SendMessage(payload);
    }

    // send updates every second
    Thread.Sleep(1000);
}

static string createPayload(int cpuTemp, int gpuTemp)
{
    string cpuStr = cpuTemp.ToString();
    if (cpuTemp < 10)
    {
        cpuStr = "0" + cpuStr;
    }

    string gpuStr = gpuTemp.ToString();
    if (gpuTemp < 10)
    {
        gpuStr = "0" + gpuStr;
    }

    return cpuStr + ":" + gpuStr;
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