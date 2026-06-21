using Microsoft.Extensions.Logging;

namespace hardware_monitor.Web;

/// <summary>
/// Single send path to the ESP32 display. Encapsulates the "prefer serial, else UDP" choice that
/// previously lived inline in the sampling loop, so both the 1 Hz telemetry loop and the web config
/// endpoint transmit the same way. The chosen transport is fixed at startup by which one opened.
/// </summary>
public class DisplaySender
{
    private readonly SerialWriter _serial;
    private readonly bool _serialAvailable;
    private readonly UdpSender _udp;
    private readonly bool _udpAvailable;
    private readonly ILogger<DisplaySender> _logger;

    public DisplaySender(
        SerialWriter serial, bool serialAvailable,
        UdpSender udp, bool udpAvailable,
        ILogger<DisplaySender> logger)
    {
        _serial = serial;
        _serialAvailable = serialAvailable;
        _udp = udp;
        _udpAvailable = udpAvailable;
        _logger = logger;
    }

    /// <summary>True when a transport is available, i.e. a payload can actually reach the display.</summary>
    public bool IsAvailable => _serialAvailable || _udpAvailable;

    /// <summary>
    /// Sends one payload to the display over the active transport (serial preferred). No-op if
    /// neither transport opened. Returns true if a transport was available to send on.
    /// </summary>
    public async Task<bool> SendAsync(string payload)
    {
        if (_serialAvailable)
        {
            _serial.SendMessage(payload);
            return true;
        }
        if (_udpAvailable)
        {
            await _udp.SendMessage(payload);
            return true;
        }
        return false;
    }
}
