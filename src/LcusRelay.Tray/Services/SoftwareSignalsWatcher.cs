using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;
using ThreadingTimer = System.Threading.Timer;

namespace LcusRelay.Tray.Services;

/// <summary>
/// Watcher generico per segnali basati su processi + stato RDP.
/// Emissione trigger:
/// - signal:rdp:on/off (se abilitato)
/// - signal:{name}:on/off per ogni SoftwareSignalDefinition
/// </summary>
public sealed class SoftwareSignalsWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly SoftwareSignalsConfig _cfg;
    private readonly Func<string, Task> _onTrigger;

    private ThreadingTimer? _timer;
    private bool _lastRdp;
    private readonly Dictionary<string, bool> _states = new(StringComparer.OrdinalIgnoreCase);

    public SoftwareSignalsWatcher(ILogger log, SoftwareSignalsConfig cfg, Func<string, Task> onTrigger)
    {
        _log = log;
        _cfg = cfg;
        _onTrigger = onTrigger;
    }

    public void Start()
    {
        if (!_cfg.Enabled)
        {
            _log.LogInformation("SoftwareSignalsWatcher disabled.");
            return;
        }

        var seconds = Math.Clamp(_cfg.PollSeconds, 1, 60);
        _lastRdp = IsRdp();

        // tick immediato
        _timer = new ThreadingTimer(_ => TickSafe(), null, TimeSpan.Zero, TimeSpan.FromSeconds(seconds));
        _log.LogInformation("SoftwareSignalsWatcher started (poll {sec}s).", seconds);
    }

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

    private void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { _log.LogError(ex, "Errore SoftwareSignalsWatcher tick"); }
    }

    private void Tick()
    {
        var rdp = IsRdp();

        if (_cfg.EmitRdpSignal && rdp != _lastRdp)
        {
            _lastRdp = rdp;
            _log.LogInformation("RDP -> {state}", rdp ? "ON" : "OFF");
            _ = Fire(rdp ? "signal:rdp:on" : "signal:rdp:off");
        }

        if (_cfg.Signals is null || _cfg.Signals.Count == 0) return;

        foreach (var s in _cfg.Signals.Where(x => x.Enabled))
        {
            var name = (s.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var active = IsAnyProcessRunning(s.ProcessNames);
            if (s.RequireRdp && !rdp) active = false;

            if (!_states.TryGetValue(name, out var prev)) prev = false;

            if (active != prev)
            {
                _states[name] = active;
                _log.LogInformation("Signal {name} -> {state}", name, active ? "ON" : "OFF");
                _ = Fire($"signal:{name}:{(active ? "on" : "off")}");
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
            _log.LogError(ex, "Errore software signal fire {trigger}", trigger);
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
        _states.Clear();
        _log.LogInformation("SoftwareSignalsWatcher disposed.");
    }
}
