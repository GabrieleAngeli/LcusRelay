using System.Text.Json;
using LcusRelay.Core.Automation;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;

namespace LcusRelay.Tray.Utils;

public sealed class RelayStateStore : IRelayStateStore
{
    private readonly ILogger<RelayStateStore> _log;
    private readonly string _path;
    private RelayStateSnapshot _snapshot;

    public RelayStateStore(ILogger<RelayStateStore> log)
    {
        _log = log;
        _path = Path.Combine(ConfigStore.GetAppDataDir(), "state.json");
        _snapshot = Load();
    }

    public RelayStateSnapshot Snapshot => _snapshot;

    public void RecordRelayChange(bool state, string trigger, string? series)
    {
        _snapshot = new RelayStateSnapshot(
            state,
            string.IsNullOrWhiteSpace(trigger) ? null : trigger.Trim(),
            string.IsNullOrWhiteSpace(series) ? null : series.Trim(),
            DateTimeOffset.UtcNow
        );

        Save();
    }

    private RelayStateSnapshot Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new RelayStateSnapshot(null, null, null, null);

            var json = File.ReadAllText(_path);
            var snap = JsonSerializer.Deserialize<RelayStateSnapshot>(json, ConfigJson.Options);
            return snap ?? new RelayStateSnapshot(null, null, null, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Impossibile leggere state.json. Riparto con stato vuoto.");
            return new RelayStateSnapshot(null, null, null, null);
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_snapshot, ConfigJson.Options);
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Impossibile salvare state.json.");
        }
    }
}
