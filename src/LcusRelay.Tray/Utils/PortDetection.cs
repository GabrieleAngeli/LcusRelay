
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LcusRelay.Tray.Utils;

public static class PortDetection
{
    private static readonly Regex ComRegex = new(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Cerca un dispositivo USB serial (CH340/USB-SERIAL) e prova a estrarre COMx.
    /// Replica la logica dello script PowerShell.
    /// </summary>
    public static string? TryFindCh340ComPort(ILogger log)
    {
        try
        {
            // Win32_PnPEntity.Name es: "USB-SERIAL CH340 (COM5)"
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = (mo["Name"] as string) ?? "";
                var pnp = (mo["PNPDeviceID"] as string) ?? "";

                var matches = name.Contains("CH340", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("USB-SERIAL", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("USB Serial", StringComparison.OrdinalIgnoreCase) ||
                              pnp.Contains(@"VID_1A86&PID_7523", StringComparison.OrdinalIgnoreCase);

                if (!matches) continue;

                var m = ComRegex.Match(name);
                if (m.Success)
                {
                    var port = m.Groups[1].Value;
                    log.LogInformation("Auto-detect CH340 -> {port} ({name})", port, name);
                    return port;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Auto-detect CH340 fallito.");
        }

        return null;
    }
}
