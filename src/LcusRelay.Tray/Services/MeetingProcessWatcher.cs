
using System.Diagnostics;
using System.IO;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;
using ThreadingTimer = System.Threading.Timer;

namespace LcusRelay.Tray.Services;

public sealed class MeetingProcessWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly MeetingSignalConfig _cfg;
    private readonly Func<bool, Task> _onMeetingStateChanged;
    private ThreadingTimer? _timer;
    private bool _isOn;

    public MeetingProcessWatcher(ILogger log, MeetingSignalConfig cfg, Func<bool, Task> onMeetingStateChanged)
    {
        _log = log;
        _cfg = cfg;
        _onMeetingStateChanged = onMeetingStateChanged;
    }

    public void Start()
    {
        if (!_cfg.Enabled)
        {
            _log.LogInformation("MeetingProcessWatcher disabled.");
            return;
        }

        var seconds = Math.Clamp(_cfg.PollSeconds, 1, 60);
        _timer = new ThreadingTimer(_ => TickSafe(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(seconds));
        _log.LogInformation("MeetingProcessWatcher started (poll {sec}s).", seconds);
    }

    private void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { _log.LogError(ex, "Errore MeetingProcessWatcher tick"); }
    }

    private void Tick()
    {
        var active = IsMeetingActive();

        if (active != _isOn)
        {
            _isOn = active;
            _log.LogInformation("Meeting signal -> {state}", active ? "ON" : "OFF");
            _ = Fire(active);
        }
    }

    private async Task Fire(bool on)
    {
        try
        {
            await _onMeetingStateChanged(on).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore meeting signal fire");
        }
    }

    private static bool IsAnyProcessRunning(IEnumerable<string> names)
    {
        foreach (var n in names ?? Array.Empty<string>())
        {
            var name = (n ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            try
            {
                foreach (var candidate in GetProcessNameCandidates(name))
                {
                    var procs = Process.GetProcessesByName(candidate);
                    if (procs.Length > 0) return true;
                }
            }
            catch
            {
                // ignore per singolo processo
            }
        }

        return false;
    }

    private bool IsMeetingActive()
    {
        var mode = (_cfg.Mode ?? "Process").Trim();
        if (string.Equals(mode, "TeamsCall", StringComparison.OrdinalIgnoreCase))
        {
            return IsTeamsCallActive();
        }

        return IsAnyProcessRunning(_cfg.ProcessNames);
    }

    private bool IsTeamsCallActive()
    {
        var keywords = _cfg.CallWindowKeywords ?? new List<string>();
        if (keywords.Count == 0)
            keywords.AddRange(new[] { "meeting", "call", "chiamata", "riunione" });

        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                var name = proc.ProcessName ?? string.Empty;
                if (!IsTeamsProcessName(name)) continue;

                var title = proc.MainWindowTitle ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title)) continue;

                foreach (var k in keywords)
                {
                    var key = (k ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (title.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool IsTeamsProcessName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var n = name.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = Path.GetFileNameWithoutExtension(n);

        return string.Equals(n, "Teams", StringComparison.OrdinalIgnoreCase)
               || string.Equals(n, "MSTeams", StringComparison.OrdinalIgnoreCase)
               || string.Equals(n, "MicrosoftTeams", StringComparison.OrdinalIgnoreCase)
               || string.Equals(n, "ms-teams", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetProcessNameCandidates(string name)
    {
        var cleaned = name.Trim();
        if (cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleaned = Path.GetFileNameWithoutExtension(cleaned);

        if (!string.IsNullOrWhiteSpace(cleaned))
            yield return cleaned;

        var noSpaces = cleaned.Replace(" ", "");
        if (!string.Equals(noSpaces, cleaned, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(noSpaces))
            yield return noSpaces;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _log.LogInformation("MeetingProcessWatcher disposed.");
    }
}
