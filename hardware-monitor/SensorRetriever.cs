
using System.Net.NetworkInformation;
using hardware_monitor.Web;
using LibreHardwareMonitor.Hardware;

public class SensorRetriever : ISensorRetriever
{
    private Computer? computer = null;
    private IHardware? cpu = null;
    private ISensor? cpuTempSensor = null;
    private IHardware? gpu = null;
    private ISensor? gpuTempSensor = null;

    // RAM (via LibreHardwareMonitor Memory hardware)
    private IHardware? memory = null;
    private ISensor? ramLoadSensor = null;

    // Network (via BCL NetworkInterface counters; delta'd between reads)
    private NetworkInterface? netInterface = null;
    private long lastBytesReceived;
    private long lastBytesSent;
    private DateTime lastNetSample = DateTime.MinValue;

    // Storage capacity (via BCL DriveInfo)
    private readonly List<DriveInfo> drives;

    public SensorRetriever(IReadOnlyList<DriveInfo>? drivesToTrack = null)
    {
        // Default to the system drive when no list is supplied.
        drives = drivesToTrack is { Count: > 0 }
            ? new List<DriveInfo>(drivesToTrack)
            : StorageDrives.Resolve(null);
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

        // RAM: locate the Memory hardware and its load (% used) sensor
        InitMemory();

        // Network: pick the active adapter for throughput measurement
        InitNetwork();

        // Storage: report which drives we'll track for capacity
        Console.WriteLine("Tracking storage for drive(s): {0}",
            string.Join(", ", drives.Select(d => d.Name)));
    }

    private void InitMemory()
    {
        if (computer == null) return;

        foreach (IHardware hardware in computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Memory)
            {
                memory = hardware;
                break;
            }
        }

        if (memory == null)
        {
            Console.WriteLine("InitMemory::no Memory hardware found");
            return;
        }

        memory.Update();
        // Prefer the load sensor explicitly named "Memory" (physical RAM %); fall back to first Load.
        foreach (ISensor sensor in memory.Sensors)
        {
            if (sensor.SensorType == SensorType.Load && sensor.Name == "Memory")
            {
                ramLoadSensor = sensor;
                break;
            }
        }
        ramLoadSensor ??= memory.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);

        Console.WriteLine(ramLoadSensor != null
            ? "Retrieved RAM load sensor"
            : "InitMemory::no RAM load sensor found");
    }

    private void InitNetwork()
    {
        netInterface = SelectActiveInterface();
        if (netInterface == null)
        {
            Console.WriteLine("InitNetwork::no active network adapter found");
            return;
        }

        Console.WriteLine("Retrieved network adapter: {0} ({1})",
            netInterface.Name, netInterface.NetworkInterfaceType);

        try
        {
            var stats = netInterface.GetIPStatistics();
            lastBytesReceived = stats.BytesReceived;
            lastBytesSent = stats.BytesSent;
            lastNetSample = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InitNetwork::failed to read initial counters: {ex.Message}");
        }
    }

    /// <summary>
    /// Picks the "currently connected" adapter: operational, not loopback/tunnel, and with an
    /// IPv4 default gateway (i.e. the one carrying real traffic). Prefers the fastest link.
    /// </summary>
    private static NetworkInterface? SelectActiveInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                        && n.GetIPProperties().GatewayAddresses
                            .Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();
    }

    private Computer OpenComputer()
    {
        Computer computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
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
        return (int)(cpuTempSensor.Value ?? 0);
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
        return (int)(gpuTempSensor.Value ?? 0);
    }

    public double GetRamUsedPercent()
    {
        if (memory == null || ramLoadSensor == null)
        {
            return 0;
        }

        memory.Update();
        return Math.Round(ramLoadSensor.Value ?? 0, 1);
    }

    public (double DownMbps, double UpMbps) GetNetworkThroughput()
    {
        if (netInterface == null)
        {
            return (0, 0);
        }

        try
        {
            var stats = netInterface.GetIPStatistics();
            long recv = stats.BytesReceived;
            long sent = stats.BytesSent;
            DateTime now = DateTime.UtcNow;

            double seconds = (now - lastNetSample).TotalSeconds;
            if (lastNetSample == DateTime.MinValue || seconds <= 0)
            {
                // First sample (or clock anomaly): establish a baseline, report nothing yet.
                lastBytesReceived = recv;
                lastBytesSent = sent;
                lastNetSample = now;
                return (0, 0);
            }

            // Counters can reset (adapter reconnect); guard against negative deltas.
            double downBytesPerSec = Math.Max(0, recv - lastBytesReceived) / seconds;
            double upBytesPerSec = Math.Max(0, sent - lastBytesSent) / seconds;

            lastBytesReceived = recv;
            lastBytesSent = sent;
            lastNetSample = now;

            // bytes/s -> megabits/s
            double downMbps = downBytesPerSec * 8 / 1_000_000;
            double upMbps = upBytesPerSec * 8 / 1_000_000;
            return (Math.Round(downMbps, 2), Math.Round(upMbps, 2));
        }
        catch
        {
            return (0, 0);
        }
    }

    public IReadOnlyList<DriveUsage> GetStorageUsage()
    {
        var result = new List<DriveUsage>(drives.Count);
        foreach (DriveInfo drive in drives)
        {
            try
            {
                if (!drive.IsReady || drive.TotalSize <= 0)
                {
                    continue;
                }

                double used = drive.TotalSize - drive.AvailableFreeSpace;
                double pct = used / drive.TotalSize * 100.0;
                result.Add(new DriveUsage(StorageDrives.Letter(drive), Math.Round(pct, 1)));
            }
            catch
            {
                // Skip a drive that became unavailable rather than failing the whole read.
            }
        }
        return result;
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