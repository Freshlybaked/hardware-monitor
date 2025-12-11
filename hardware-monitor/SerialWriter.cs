using System.IO.Ports;

public class SerialWriter
{
    SerialPort? serialPort = null;

    readonly int baudRate = 115200;

    public SerialWriter()
    {
        
    }

    public bool TryOpenPort()
    {
        string[] ports = SerialPort.GetPortNames();
        if (ports.Length == 0)
        {
            Console.WriteLine("No display device connected. Exiting...");
            Console.ReadLine();
            return false;
        }

        if (ports.Length == 1)
        {
            serialPort = new SerialPort(ports[0], baudRate, Parity.None, 8, StopBits.One);
        }
        else
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
                        if (parsedVal >= 1 && parsedVal <= ports.Length)
                        {
                            comPortSelected = parsedVal - 1;
                            serialPort = new SerialPort(ports[comPortSelected], baudRate, Parity.None, 8, StopBits.One);
                            Console.WriteLine("COM port {0} selected.", ports[comPortSelected]);
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
        }

        if (serialPort != null)
        {
            try
            {
                serialPort.Open();
            }
            catch (Exception)
            {
                Console.WriteLine("Unable to open serial port with port name {0}", serialPort.PortName);
                return false;
            }

        }
        else
        {
            Console.WriteLine("Unable to retrieve a connected display device. Exiting...");
            return false;
        }

        Console.WriteLine("Successfully opened serial port with port name {0}", serialPort.PortName);
        return true;
    }

    public void SendMessage(string payload)
    {
        if(serialPort == null)
        {
            Console.WriteLine("SerialWriter::SendMessage::serialPort is null");
            return;
        }

        serialPort.Write(payload + "\n");
    }
}