using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace LcusRelay.Tray.Services;

public sealed class SessionWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly SessionConfig _cfg;
    private readonly Func<string, Task> _onTrigger;
    private SessionNotificationWindow? _window;

    private string? _lastTrigger;
    private DateTimeOffset _lastTriggerAt;
    private readonly TimeSpan _dedupeWindow = TimeSpan.FromMilliseconds(700);

    public SessionWatcher(ILogger log, SessionConfig cfg, Func<string, Task> onTrigger)
    {
        _log = log;
        _cfg = cfg ?? new SessionConfig();
        _onTrigger = onTrigger;
    }

    public void Start()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;

        _window = new SessionNotificationWindow(_log, this);
        _window.Start();

        _log.LogInformation("SessionWatcher started (SystemEvents + WTS notifications).");
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        var trigger = MapSystemEvent(e.Reason);
        if (trigger is null) return;

        _log.LogInformation("Session event da SystemEvents: {reason} -> {trigger}", e.Reason, trigger);
        FireWithDedupe(trigger);
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        _log.LogInformation("SessionEnding ricevuto ({reason}) -> session:logoff", e.Reason);
        FireWithDedupe("session:logoff");
    }

    internal void OnWtsSessionEvent(int evt)
    {
        var trigger = MapWtsEvent(evt);
        if (trigger is null) return;

        _log.LogInformation("Session event da WTS: {evt} -> {trigger}", evt, trigger);
        FireWithDedupe(trigger);
    }

    private void FireWithDedupe(string trigger)
    {
        if (IsRdp())
        {
            if (_cfg.SuppressLogonUnlockWhenRdp
                && (string.Equals(trigger, "session:logon", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trigger, "session:unlock", StringComparison.OrdinalIgnoreCase)))
            {
                _log.LogInformation("Session event {trigger} ignorato (RDP attivo).", trigger);
                return;
            }

            if (_cfg.SuppressLockWhenRdp
                && string.Equals(trigger, "session:lock", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInformation("Session event {trigger} ignorato (RDP attivo).", trigger);
                return;
            }
        }

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastTrigger, trigger, StringComparison.OrdinalIgnoreCase)
            && (now - _lastTriggerAt) <= _dedupeWindow)
        {
            _log.LogDebug("Evento duplicato ignorato: {trigger}", trigger);
            return;
        }

        _lastTrigger = trigger;
        _lastTriggerAt = now;

        _ = Task.Run(() => SafeFire(trigger));
    }

    private async Task SafeFire(string trigger)
    {
        try
        {
            await _onTrigger(trigger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore SessionWatcher trigger {trigger}", trigger);
        }
    }

    private static string? MapSystemEvent(SessionSwitchReason reason)
        => reason switch
        {
            SessionSwitchReason.SessionLogon => "session:logon",
            SessionSwitchReason.SessionLogoff => "session:logoff",
            SessionSwitchReason.SessionLock => "session:lock",
            SessionSwitchReason.SessionUnlock => "session:unlock",
            SessionSwitchReason.RemoteConnect => "session:remoteconnect",
            SessionSwitchReason.RemoteDisconnect => "session:remotedisconnect",
            _ => null
        };

    private static string? MapWtsEvent(int evt)
        => evt switch
        {
            SessionNotificationWindow.WTS_SESSION_LOGON => "session:logon",
            SessionNotificationWindow.WTS_SESSION_LOGOFF => "session:logoff",
            SessionNotificationWindow.WTS_SESSION_LOCK => "session:lock",
            SessionNotificationWindow.WTS_SESSION_UNLOCK => "session:unlock",
            SessionNotificationWindow.WTS_REMOTE_CONNECT => "session:remoteconnect",
            SessionNotificationWindow.WTS_REMOTE_DISCONNECT => "session:remotedisconnect",
            _ => null
        };

    private static bool IsRdp()
    {
        try
        {
            return SystemInformation.TerminalServerSession;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.SessionEnding -= OnSessionEnding;

        _window?.Dispose();
        _window = null;

        _log.LogInformation("SessionWatcher disposed.");
    }

    private sealed class SessionNotificationWindow : NativeWindow, IDisposable
    {
        private readonly ILogger _log;
        private readonly SessionWatcher _owner;
        private bool _registered;

        public const int WM_WTSSESSION_CHANGE = 0x02B1;

        public const int WTS_CONSOLE_CONNECT = 0x1;
        public const int WTS_CONSOLE_DISCONNECT = 0x2;
        public const int WTS_REMOTE_CONNECT = 0x3;
        public const int WTS_REMOTE_DISCONNECT = 0x4;
        public const int WTS_SESSION_LOGON = 0x5;
        public const int WTS_SESSION_LOGOFF = 0x6;
        public const int WTS_SESSION_LOCK = 0x7;
        public const int WTS_SESSION_UNLOCK = 0x8;

        private const int NOTIFY_FOR_THIS_SESSION = 0;

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

        public SessionNotificationWindow(ILogger log, SessionWatcher owner)
        {
            _log = log;
            _owner = owner;
        }

        public void Start()
        {
            CreateHandle(new CreateParams
            {
                Caption = "LcusRelay.SessionWindow"
            });

            _registered = WTSRegisterSessionNotification(Handle, NOTIFY_FOR_THIS_SESSION);
            if (!_registered)
            {
                var err = Marshal.GetLastWin32Error();
                _log.LogWarning("WTSRegisterSessionNotification fallita. Win32Error={err}", err);
            }
            else
            {
                _log.LogInformation("WTS session notification registrata su HWND {hwnd}", Handle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_WTSSESSION_CHANGE)
            {
                var evt = m.WParam.ToInt32();
                _owner.OnWtsSessionEvent(evt);
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try
            {
                if (_registered && Handle != IntPtr.Zero)
                {
                    WTSUnRegisterSessionNotification(Handle);
                    _registered = false;
                }
            }
            catch { }

            try
            {
                if (Handle != IntPtr.Zero)
                    DestroyHandle();
            }
            catch { }
        }
    }
}
