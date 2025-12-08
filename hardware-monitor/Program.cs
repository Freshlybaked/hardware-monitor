using System.Collections;
using System.Diagnostics;
using System.IO.Ports;
using System.Reflection;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

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

// PawnIO successfully found, go ahead to retrieve devices
Console.WriteLine("Retrieving devices...");
Computer computer = OpenComputer();

// CPU retrieval
IHardware? cpu = GetCPU(computer);
if (cpu == null)
{
    Console.Write("Unable to retrieve CPU");
    return;
}
Console.WriteLine("Retrieved CPU: {0}", cpu.Name);

// GPU listing
IHardware? gpu = null;
List<IHardware> gpuList = ListGPUs(computer);
if (gpuList.Count == 1)
{
    gpu = gpuList[0];
}
else
{
    // prompt user for which one to use
    Console.WriteLine("Multiple GPUs detected. Please input the number of the GPU to use and press enter.");
    int idx = 1;
    foreach (IHardware hardware in gpuList)
    {
        Console.WriteLine("{0}: GPU Name: {1} - GPU Identifier: {2}", idx++, hardware.Name, hardware.Identifier);
    }

    int gpuSelected = -1;
    while (gpuSelected == -1)
    {
        string? input = Console.ReadLine();
        if (input != null)
        {
            try
            {
                int parsedVal = int.Parse(input);
                if(parsedVal >= 1 && parsedVal <= gpuList.Count)
                {
                    gpu = gpuList[parsedVal-1];
                    Console.WriteLine("GPU {0} selected.", gpu.Name);
                    gpuSelected = parsedVal;
                }
            } catch(Exception)
            {
                continue;
            }
        }
    }
}

if (gpu == null)
{
    Console.Write("Unable to retrieve GPU");
    return;
}
Console.WriteLine("Retrieved GPU: {0}", gpu.Name);

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

if(serialPort != null)
{
    try
    {
        serialPort.Open();
    }
    catch(Exception)
    {
        Console.WriteLine("Unable to open serial port with port name {0}", serialPort.PortName);
        Console.ReadLine();
        return;
    }
    
} else
{
    Console.WriteLine("Unable to retrieve a connected display device. Exiting...");
    Console.ReadLine();
    return;
}

Console.WriteLine("Successfully opened serial port with port name {0}", serialPort.PortName);

while (true)
{
    // cpu = GetCPU(computer);
    int cpuTemp = GetCPUTemp(cpu);

    // gpu = GetGPU(computer, HardwareType.GpuNvidia);
    int gpuTemp = GetGPUTemp(gpu);

    // Console.WriteLine("Retrieved cpu temp:{0} and gpu temp:{1}", cpuTemp, gpuTemp);

    WriteToSerial(serialPort, cpuTemp, gpuTemp);

    Thread.Sleep(1000);
}

static void WriteToSerial(SerialPort serialPort, int cpuTemp, int gpuTemp)
{
    String cpuStr = cpuTemp.ToString();
    if (cpuTemp < 10)
    {
        cpuStr = "0" + cpuStr;
    }

    String gpuStr = gpuTemp.ToString();
    if (gpuTemp < 10)
    {
        gpuStr = "0" + gpuStr;
    }

    serialPort.Write(cpuStr + ":" + gpuStr + "\n");
}

static Computer OpenComputer()
{
    Computer computer = new Computer
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = false,
        IsMotherboardEnabled = false,
        IsControllerEnabled = false,
        IsNetworkEnabled = false,
        IsStorageEnabled = false
    };

    computer.Open();
    computer.Accept(new UpdateVisitor());

    return computer;
}
static IHardware? GetCPU(Computer computer)
{
    foreach (IHardware hardware in computer.Hardware)
    {
        if (hardware.HardwareType == HardwareType.Cpu)
        {
            return hardware;
        }
    }

    return null;
}

static List<IHardware> ListGPUs(Computer computer)
{
    List<IHardware> result = new List<IHardware>();
    foreach (IHardware hardware in computer.Hardware)
    {
        if (hardware.HardwareType == HardwareType.GpuNvidia
            || hardware.HardwareType == HardwareType.GpuIntel
            || hardware.HardwareType == HardwareType.GpuAmd)
        {
            result.Add(hardware);
        }
    }

    return result;
}

static int GetCPUTemp(IHardware hardware)
{
    hardware.Update();
    foreach (ISensor sensor in hardware.Sensors)
    {
        if (sensor.SensorType == SensorType.Temperature)
        {
            if (sensor.Value.HasValue)
            {
                return (int)sensor.Value;
            }

            return 0;
        }
    }

    return 0;
}

static int GetGPUTemp(IHardware hardware)
{
    hardware.Update();
    foreach (ISensor sensor in hardware.Sensors)
    {
        if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core"))
        {
            if (sensor.Value.HasValue)
            {
                return (int)sensor.Value;
            }

            return 0;
        }
    }

    return 0;
}

public class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer)
    {
        computer.Traverse(this);
    }
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

