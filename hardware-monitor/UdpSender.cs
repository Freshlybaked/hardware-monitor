using System.Net.Sockets;
using System.Text;
using Zeroconf;

public class UdpSender
{
    readonly UdpClient udpClient = new();
    string deviceIp = "";
    int devicePort = 0;

    readonly string discoveryUrl;
    readonly string hostName;
    readonly string serviceName;

    public UdpSender(string inDiscoveryUrl, string inHostName, string inServiceName)
    {
        discoveryUrl = inDiscoveryUrl;
        hostName = inHostName;
        serviceName = inServiceName;
    }

    public async Task<bool> FindEsp32Ip()
    {
        var results = await ZeroconfResolver.ResolveAsync(discoveryUrl);
        if(results == null)
        {
            Console.WriteLine("UdpSender::FindEsp32Ip::results is null");
            return false;
        }

        IZeroconfHost? sensorDisplayHost = results.First(host => host.DisplayName == hostName);
        if(sensorDisplayHost == null)
        {
            Console.WriteLine("UdpSender::FindEsp32Ip::sensorDisplayHost is null");
            return false;
        }
        deviceIp = sensorDisplayHost.IPAddress;

        IService sensorDisplayService = sensorDisplayHost.Services[hostName + "." + discoveryUrl];
        if(sensorDisplayService == null)
        {
            Console.WriteLine("UdpSender::FindEsp32Ip::sensorDisplayService is null");
            return false;
        }
        devicePort = sensorDisplayService.Port;

        Console.WriteLine($"Found: {sensorDisplayHost.DisplayName} at {deviceIp}:{devicePort}");

        return true;
    }

    public async Task SendMessage(string message)
    {
        var data = Encoding.UTF8.GetBytes(message);
        await udpClient.SendAsync(data, data.Length, deviceIp, devicePort);
    }
}