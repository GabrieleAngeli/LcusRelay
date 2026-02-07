
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LcusRelay.Tray.Utils;

public static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LcusRelay";

    public static void EnableForCurrentUser(ILogger log)
    {
        var exePath = Application.ExecutablePath;

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(AppName, $"\"{exePath}\"");
        log.LogInformation("Start with Windows abilitato: {exe}", exePath);
    }

    public static void DisableForCurrentUser(ILogger log)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
        log.LogInformation("Start with Windows disabilitato.");
    }
}
