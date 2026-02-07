using System.Runtime.InteropServices;

namespace LcusRelay.Tray.Utils;

internal static class ConsoleBootstrap
{
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    public static bool TryAttachToParentConsole()
    {
        try
        {
            if (GetConsoleWindow() != nint.Zero) return true;
            return AttachConsole(ATTACH_PARENT_PROCESS);
        }
        catch
        {
            return false;
        }
    }
}
