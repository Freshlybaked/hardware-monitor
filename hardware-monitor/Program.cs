using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

Console.WriteLine("Retrieving devices...");
Computer computer = OpenComputer();

IHardware cpu = GetCPU(computer);
if (cpu == null)
{
    Console.Write("Unable to retrieve CPU");
    return;
}
Console.WriteLine("Retrieved CPU: {0}", cpu.Name);

IHardware gpu = GetGPU(computer);
if (gpu == null)
{
    Console.Write("Unable to retrieve GPU");
    return;
}
Console.WriteLine("Retrieved GPU: {0}", gpu.Name);

while (true)
{
    cpu = GetCPU(computer);
    int cpuTemp = GetCPUTemp(cpu);

    gpu = GetGPU(computer);
    int gpuTemp = GetGPUTemp(gpu);

    Console.WriteLine("Retrieved cpu temp:{0} and gpu temp:{1}", cpuTemp, gpuTemp);



    Thread.Sleep(1000);
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
static IHardware GetCPU(Computer computer)
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

static IHardware GetGPU(Computer computer)
{
    foreach (IHardware hardware in computer.Hardware)
    {
        if (hardware.HardwareType == HardwareType.GpuNvidia)
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
            float temp = (float)sensor.Value;
            return (int)temp;
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

