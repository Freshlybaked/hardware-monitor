using System.Collections;
using System.Diagnostics;
using System.IO.Ports;
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
// SerialPort serialPort = new SerialPort("COM3", 115200, Parity.None, 8, StopBits.One);
// serialPort.Open();

while (true)
{
    // cpu = GetCPU(computer);
    int cpuTemp = GetCPUTemp(cpu);

    // gpu = GetGPU(computer, HardwareType.GpuNvidia);
    int gpuTemp = GetGPUTemp(gpu);

    Console.WriteLine("Retrieved cpu temp:{0} and gpu temp:{1}", cpuTemp, gpuTemp);

    // WriteToSerial(serialPort, cpuTemp, gpuTemp);

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

// static IHardware? GetGPU(Computer computer)
// {
//     foreach (IHardware hardware in computer.Hardware)
//     {
//         if (hardware.HardwareType == HardwareType.GpuNvidia)
//         {
//             return hardware;
//         }
//     }

//     return null;
// }

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

static IHardware? GetGPU(Computer computer, HardwareType type)
{
    foreach (IHardware hardware in computer.Hardware)
    {
        if (hardware.HardwareType == type)
        {
            return hardware;
        }
    }

    return null;
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
            float temp = (float)sensor.Value;
            return (int)temp;
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

