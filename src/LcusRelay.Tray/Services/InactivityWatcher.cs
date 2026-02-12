using System.Runtime.InteropServices;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;
using ThreadingTimer = System.Threading.Timer;

namespace LcusRelay.Tray.Services;

public sealed class InactivityWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly InactivityConfig _cfg;
    private readonly Func<string, Task> _onTrigger;

    private ThreadingTimer? _timer;
    private bool _isIdle;

    public InactivityWatcher(ILogger log, InactivityConfig cfg, Func<string, Task> onTrigger)
    {
        _log = log;
        _cfg = cfg ?? new InactivityConfig();
        _onTrigger = onTrigger;
    }

    public void Start()
    {
        if (!_cfg.Enabled)
        {
            _log.LogInformation("InactivityWatcher disabled.");
            return;
        }

        var pollSeconds = Math.Clamp(_cfg.PollSeconds, 1, 60);
        _timer = new ThreadingTimer(_ => TickSafe(), null, TimeSpan.Zero, TimeSpan.FromSeconds(pollSeconds));
        _log.LogInformation("InactivityWatcher started (idle={min}m, poll={poll}s).", Math.Clamp(_cfg.IdleMinutes, 1, 1440), pollSeconds);
    }

    private void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { _log.LogError(ex, "Errore InactivityWatcher tick"); }
    }

    private void Tick()
    {
        var idleMs = GetIdleMilliseconds();
        if (idleMs is null) return;

        var thresholdMs = Math.Clamp(_cfg.IdleMinutes, 1, 1440) * 60_000;
        var nowIdle = idleMs.Value >= thresholdMs;

        if (nowIdle && !_isIdle)
        {
            _isIdle = true;
            _log.LogInformation("Inattivita rilevata ({sec}s) -> system:idle", idleMs.Value / 1000);
            _ = Fire("system:idle");
            return;
        }

        if (!nowIdle && _isIdle)
        {
            _isIdle = false;
            if (_cfg.EmitActiveOnReturn)
            {
                _log.LogInformation("Attivita utente ripresa -> system:active");
                _ = Fire("system:active");
            }
        }
    }

    private async Task Fire(string trigger)
    {
        try
        {
            await _onTrigger(trigger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore inactivity fire {trigger}", trigger);
        }
    }

    private static int? GetIdleMilliseconds()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>();
        if (!GetLastInputInfo(ref info))
            return null;

        var tickNow = unchecked((uint)Environment.TickCount);
        var idle = unchecked(tickNow - info.dwTime);
        return (int)idle;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _log.LogInformation("InactivityWatcher disposed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
