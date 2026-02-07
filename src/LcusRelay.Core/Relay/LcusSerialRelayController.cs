using Microsoft.Extensions.Logging;

using System.IO.Ports;

namespace LcusRelay.Core.Relay;

/// <summary>
/// Implementazione "LCUS-1" via seriale.
/// Protocollo: A0 <addr> <cmd> <checksum>, cmd: 0x01 ON, 0x00 OFF.
/// Checksum: (A0 + addr + cmd) & 0xFF.
/// </summary>
public sealed class LcusSerialRelayController : IRelayController
{
    private readonly SerialRelaySettings _settings;
    private readonly ILogger<LcusSerialRelayController>? _log;
    private bool? _last;

    public LcusSerialRelayController(SerialRelaySettings settings, ILogger<LcusSerialRelayController>? log = null)
    {
        _settings = settings;
        _log = log;
    }

    public bool? LastKnownState => _last;

    public async Task SetAsync(bool on, CancellationToken cancellationToken = default)
    {
        var portName = _settings.PortName;
        if (string.IsNullOrWhiteSpace(portName))
            throw new InvalidOperationException("PortName non configurato.");

        var packet = BuildPacket(_settings.Address, on);
        _log?.LogInformation("Invio relay su porta {port}: address={address}, state={state}, bytes={bytes}", portName, _settings.Address, on ? "On" : "Off", BitConverter.ToString(packet));

        // SerialPort non ha API truly-async: usiamo Task.Run per non bloccare UI.
        await Task.Run(() =>
        {
            var lastError = (Exception?)null;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    if (attempt > 1)
                        Thread.Sleep(400);

                    using var sp = new SerialPort(portName, _settings.BaudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = _settings.TimeoutMs,
                        WriteTimeout = _settings.TimeoutMs,
                        DtrEnable = true,
                        RtsEnable = true
                    };

                    sp.Open();
                    Thread.Sleep(50);
                    sp.Write(packet, 0, packet.Length);
                    Thread.Sleep(50);
                    sp.Close();
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _log?.LogWarning(ex, "Tentativo {attempt}/2 fallito su porta {port}.", attempt, portName);
                }
            }

            throw new InvalidOperationException(
                $"Impossibile comunicare con la porta {portName}. " +
                "Verifica la chiavetta USB/seriale e riprova.",
                lastError);
        }, cancellationToken).ConfigureAwait(false);

        _last = on;
        _log?.LogInformation("Relay inviata con successo. LastKnownState={state}", _last == true ? "On" : "Off");
    }

    public static byte[] BuildPacket(int address, bool on)
    {
        if (address is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(address));

        byte start = 0xA0;
        byte addr = (byte)address;
        byte cmd = on ? (byte)0x01 : (byte)0x00;
        byte chk = (byte)((start + addr + cmd) & 0xFF);

        return new[] { start, addr, cmd, chk };
    }
}

public sealed class SerialRelaySettings
{
    public required string PortName { get; init; }
    public int Address { get; init; } = 1;
    public int BaudRate { get; init; } = 9600;
    public int TimeoutMs { get; init; } = 500;
}
