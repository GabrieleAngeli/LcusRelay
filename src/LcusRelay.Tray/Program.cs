using LcusRelay.Tray;
using LcusRelay.Tray.Utils;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace LcusRelay.TrayApp;


[SupportedOSPlatform("windows")]
internal static class Program
{
    private static Mutex? _mutex;
    
    [STAThread]
    static void Main()
    {
        var allowElevated = string.Equals(
            Environment.GetEnvironmentVariable("LCUSRELAY_ALLOW_ELEVATED"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        if (IsRunningAsAdmin() && !allowElevated)
        {
            if (TryRelaunchAsUser())
                return;
        }

        _mutex = new Mutex(true, @"Local\LcusRelay.Tray.Singleton", out var isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());

        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRelaunchAsUser()
    {
        try
        {
            var exe = Application.ExecutablePath;
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{exe}\"",
                UseShellExecute = true
            };

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
