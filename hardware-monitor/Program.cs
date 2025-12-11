using System.Collections;
using System.Diagnostics;
using System.IO.Ports;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using Zeroconf;

// This application requires PawnIO to be installed
if (!PawnIo.IsInstalled)
{
    Console.WriteLine("PawnIO not installed. This console application requires PawnIO to be installed to run. Press enter to open the PawnIO website in your browser and terminate this application.");
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

// Init sensor retriever which retrieves GPU and GPU instances
SensorRetriever sensorRetriever = new SensorRetriever();
sensorRetriever.Init();

// establish serial port
string[] ports = SerialPort.GetPortNames();
SerialPort? serialPort = null;
if(ports.Length == 0)
{
    Console.WriteLine("No display device connected. Exiting...");
    Console.ReadLine();
    return;
}

if(ports.Length == 1)
{
    serialPort = new SerialPort(ports[0], 115200, Parity.None, 8, StopBits.One);
} else
{
    // prompt user for which COM port to use
    Console.WriteLine("Multiple COM ports detected. Please input the number of the COM port to use and press enter.");
    int idx = 1;
    foreach (string port in ports)
    {
        Console.WriteLine("{0}: COM Port: {1}", idx++, port);
    }

    int comPortSelected = -1;
    while (comPortSelected == -1)
    {
        string? input = Console.ReadLine();
        if (input != null)
        {
            try
            {
                int parsedVal = int.Parse(input);
                if(parsedVal >= 1 && parsedVal <= ports.Length)
                {
                    comPortSelected = parsedVal - 1;
                    serialPort = new SerialPort(ports[comPortSelected], 115200, Parity.None, 8, StopBits.One);
                    Console.WriteLine("COM port {0} selected.", ports[comPortSelected]);
                }
            } catch(Exception)
            {
                continue;
            }
        }
    }
}

// if(serialPort != null)
// {
//     try
//     {
//         serialPort.Open();
//     }
//     catch(Exception)
//     {
//         Console.WriteLine("Unable to open serial port with port name {0}", serialPort.PortName);
//         Console.ReadLine();
//         return;
//     }
    
// } else
// {
//     Console.WriteLine("Unable to retrieve a connected display device. Exiting...");
//     Console.ReadLine();
//     return;
// }

// Console.WriteLine("Successfully opened serial port with port name {0}", serialPort.PortName);

// establish udp connection
UdpClient udpClient = new UdpClient();
String espIp = await FindEsp32Ip();

if(espIp == null)
{
    Console.WriteLine("ESP32 Ip Not Found");
    Console.ReadLine();
    return;
}

while (true)
{
    // cpu = GetCPU(computer);
    int cpuTemp = sensorRetriever.GetCPUTemp();

    // gpu = GetGPU(computer, HardwareType.GpuNvidia);
    int gpuTemp = sensorRetriever.GetGPUTemp();

    // Console.WriteLine("Retrieved cpu temp:{0} and gpu temp:{1}", cpuTemp, gpuTemp);
    string payload = createPayload(cpuTemp, gpuTemp);

    // WriteToSerial(serialPort, payload);
    Console.WriteLine($"Created payload {payload}");
    await SendUdpMessage(udpClient, espIp, 4210, payload);

    Thread.Sleep(1000);
}

static async Task<string> FindEsp32Ip()
{
    // look for service advertised by ESP32
    var results = await ZeroconfResolver.ResolveAsync("_esp32udp._udp.local.");

    Console.WriteLine($"results: {results}");

    foreach (var host in results)
    {
        Console.WriteLine($"Found: {host.DisplayName} - {host.IPAddress}");
        return host.IPAddress;   // return first device found
    }

    return null;
}

static async Task SendUdpMessage(UdpClient client, string ip, int port, string message)
{
    var data = Encoding.UTF8.GetBytes(message);
    await client.SendAsync(data, data.Length, ip, port);

    Console.WriteLine($"Sent '{message}' to {ip}:{port}");
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

static void WriteToSerial(SerialPort serialPort, string payload)
{
    serialPort.Write(payload + "\n");
}