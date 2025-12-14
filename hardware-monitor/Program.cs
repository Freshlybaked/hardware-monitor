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

// Init sensor retriever which retrieves CPU and GPU instances
SensorRetriever sensorRetriever = new();
sensorRetriever.Init();

// try to retrieve serial port
SerialWriter serialWriter = new();
bool serialAvailable = serialWriter.TryOpenPort();
Console.WriteLine($"Is Serial Available: {serialAvailable}");

// try to retrieve udp port
UdpSender udpSender = new("_esp32udp._udp.local.", "sensordisplay", "_esp32udp");
bool udpAvailable = await udpSender.FindEsp32Ip();
Console.WriteLine($"Is UDP Available: {udpAvailable}");

while (true)
{
    int cpuTemp = sensorRetriever.GetCPUTemp();
    int gpuTemp = sensorRetriever.GetGPUTemp();

    string payload = createPayload(cpuTemp, gpuTemp);

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