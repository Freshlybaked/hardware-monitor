
using LibreHardwareMonitor.Hardware;

public class SensorRetriever
{
    private Computer? computer = null;
    private IHardware? cpu = null;
    private ISensor? cpuTempSensor = null;
    private IHardware? gpu = null;
    private ISensor? gpuTempSensor = null;
    public SensorRetriever()
    {

    }

    public void Init()
    {
        Console.WriteLine("Retrieving devices...");
        computer = OpenComputer();

        // CPU retrieval
        cpu = GetCPU();
        if (cpu == null)
        {
            Console.Write("Unable to retrieve CPU");
            return;
        }
        Console.WriteLine("Retrieved CPU: {0}", cpu.Name);

        // Retrieve CPU temp sensor
        cpuTempSensor = GetCPUTempSensor();

        // GPU listing
        // gpu = null;
        List<IHardware> gpuList = ListGPUs();
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
                        if (parsedVal >= 1 && parsedVal <= gpuList.Count)
                        {
                            gpu = gpuList[parsedVal - 1];
                            Console.WriteLine("GPU {0} selected.", gpu.Name);
                            gpuSelected = parsedVal;
                        }
                    }
                    catch (Exception)
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

        // retrieve GPU temp sensor
        gpuTempSensor = GetGPUTempSensor();
    }

    private Computer OpenComputer()
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
    private IHardware? GetCPU()
    {
        if(computer == null)
        {
            Console.WriteLine("GetCPU::computer is null");
            return null;
        }

        foreach (IHardware hardware in computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Cpu)
            {
                return hardware;
            }
        }

        return null;
    }

    private ISensor? GetCPUTempSensor()
    {
        if(cpu == null)
        {
            Console.WriteLine("GetCPUTempSensor::cpu is null");
            return null;
        }

        foreach (ISensor sensor in cpu.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature)
            {
                return sensor;
            }
        }

        return null;
    }

    public int GetCPUTemp()
    {
        if(cpu == null)
        {
            Console.WriteLine("GetCPUTemp::cpu is null");
            return 0;
        }

        if(cpuTempSensor == null)
        {
            Console.WriteLine("GetCPUTemp::cpuTempSensor is null");
            return 0;
        }

        cpu.Update();
        return (int)cpuTempSensor.Value;
        // var sensors = cpu?.Sensors;
        // foreach (ISensor sensor in sensors)
        // {
        //     if (sensor.SensorType == SensorType.Temperature)
        //     {
        //         if (sensor.Value.HasValue)
        //         {
        //             return (int)sensor.Value;
        //         }

        //         return 0;
        //     }
        // }

        // return 0;
    }

     private List<IHardware> ListGPUs()
    {
        List<IHardware> result = new List<IHardware>();
        if(computer == null)
        {
            Console.WriteLine("ListGPUs::computer is null");
            return result;
        }

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

    private ISensor? GetGPUTempSensor()
    {
        if(gpu == null)
        {
            Console.WriteLine("GetGPUTempSensor::gpu is null");
            return null;
        }

        foreach (ISensor sensor in gpu.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature)
            {
                return sensor;
            }
        }

        return null;
    }

    public int GetGPUTemp()
    {
        if(gpu == null)
        {
            Console.WriteLine("GetGPUTemp::gpu is null");
            return 0;
        }

        if(gpuTempSensor == null)
        {
            Console.WriteLine("GetGPUTemp::gpuTempSensor is null");
            return 0;
        }

        gpu.Update();
        return (int)gpuTempSensor.Value;
        // foreach (ISensor sensor in hardware.Sensors)
        // {
        //     if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core"))
        //     {
        //         if (sensor.Value.HasValue)
        //         {
        //             return (int)sensor.Value;
        //         }

        //         return 0;
        //     }
        // }

        // return 0;
    }
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