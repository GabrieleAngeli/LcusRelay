
using System.Diagnostics;
using System.Text.Json;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;

namespace LcusRelay.Tray.Utils;

public static class ConfigStore
{
    private const string AppFolderName = "LcusRelay";
    private const string ConfigFileName = "config.json";

    public static string GetAppDataDir()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetConfigPath()
        => Path.Combine(GetAppDataDir(), ConfigFileName);

    public static AppConfig LoadOrCreateDefault(ILogger log)
    {
        var path = GetConfigPath();

        if (!File.Exists(path))
        {
            var cfg = CreateDefault();
            Save(log, cfg);
            return cfg;
        }

        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, ConfigJson.Options);
            if (cfg is null) throw new InvalidOperationException("config.json vuoto o non valido.");
            var changed = ApplyMigrations(cfg);
            if (changed) Save(log, cfg);
            return cfg;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Errore nel parsing di config.json. Creo un default (backup dell'attuale).");

            try
            {
                var backup = path + ".broken." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(path, backup, overwrite: true);
            }
            catch { /* ignore */ }

            var cfg = CreateDefault();
            Save(log, cfg);
            return cfg;
        }
    }

    public static void Save(ILogger log, AppConfig cfg)
    {
        var path = GetConfigPath();
        var json = JsonSerializer.Serialize(cfg, ConfigJson.Options);
        File.WriteAllText(path, json);
        log.LogInformation("Config salvata: {path}", path);
    }

    public static bool TryOpenConfigInEditor(ILogger log, out string? error)
    {
        error = null;

        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path))
                Save(log, CreateDefault());

            log.LogInformation("Apro config.json: {path}", path);

            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });

                if (p is not null)
                    return true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Shell open fallito, provo con VS Code/Notepad.");
            }

            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                });

                if (p is not null)
                    return true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "VS Code fallito, provo con Notepad.");
            }

            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                });

                if (p is not null)
                    return true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Notepad fallito, provo con Explorer.");
            }

            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });

                if (p is not null)
                    return true;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Impossibile aprire config.json (Explorer fallito).");
                error = ex.Message;
                return false;
            }

            error = "Nessun editor disponibile o associazione file mancante.";
            return false;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Impossibile aprire config.json");
            error = ex.Message;
            return false;
        }
    }


    private static bool ApplyMigrations(AppConfig cfg)
    {
        var changed = false;

        cfg.MeetingSignal ??= new MeetingSignalConfig();
        cfg.SoftwareSignals ??= new SoftwareSignalsConfig();
        cfg.Session ??= new SessionConfig();
        cfg.Update ??= new UpdateConfig();

        changed |= EnsureRule(cfg, "session:lock", "Off", enabledByDefault: true);
        changed |= EnsureRule(cfg, "session:unlock", "On", enabledByDefault: true);
        changed |= EnsureSeries(cfg, "session:logon", "session");
        changed |= EnsureSeries(cfg, "session:logoff", "session");
        changed |= EnsureSeries(cfg, "session:lock", "session");
        changed |= EnsureSeries(cfg, "session:unlock", "session");
        changed |= EnsureAllowListIfOnRule(cfg, "session:unlock", new[] { "session" });

        // Meeting process names: include common Teams variants
        changed |= EnsureProcessNames(cfg.MeetingSignal, new[]
        {
            "Teams",
            "ms-teams",
            "MSTeams",
            "MicrosoftTeams",
            "Microsoft Teams"
        });

        if (string.IsNullOrWhiteSpace(cfg.MeetingSignal.Mode))
        {
            cfg.MeetingSignal.Mode = "Process";
            changed = true;
        }

        cfg.MeetingSignal.CallWindowKeywords ??= new List<string>();
        if (cfg.MeetingSignal.CallWindowKeywords.Count == 0)
        {
            cfg.MeetingSignal.CallWindowKeywords.AddRange(new[] { "meeting", "call", "chiamata", "riunione" });
            changed = true;
        }

        // Ninja defaults (segnali)
        var ninja = GetOrCreateNinjaSignal(cfg);
        if (ninja.Enabled && (ninja.ProcessNames is null || ninja.ProcessNames.Count == 0))
        {
            ninja.ProcessNames = new List<string>
            {
                "NinjaRMMAgent",
                "NinjaRMMAgentPatcher",
                "NinjaRemote",
                "NinjaRMM"
            };
            changed = true;
        }

        // Blink on Ninja remote access
        changed |= EnsureRule(cfg, "signal:ninja-desktop:on", enabledByDefault: true, new BlinkActionConfig
        {
            Count = 3,
            OnMs = 250,
            OffMs = 250,
            RestoreInitialState = true
        });

        return changed;
    }

    private static bool EnsureRule(AppConfig cfg, string trigger, string relayState, bool enabledByDefault)
    {
        if (cfg.Rules.Any(r => string.Equals(r.Trigger?.Trim(), trigger, StringComparison.OrdinalIgnoreCase)))
            return false;

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = trigger,
            Enabled = enabledByDefault,
            Actions = new List<ActionConfig>
            {
                new RelayActionConfig { State = relayState }
            }
        });

        return true;
    }

    private static bool EnsureSeries(AppConfig cfg, string trigger, string series)
    {
        var rule = cfg.Rules.FirstOrDefault(r => string.Equals(r.Trigger?.Trim(), trigger, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return false;

        var changed = false;

        if (string.IsNullOrWhiteSpace(rule.Series))
        {
            rule.Series = series;
            changed = true;
        }

        return changed;
    }

    private static bool EnsureAllowListIfOnRule(AppConfig cfg, string trigger, IEnumerable<string> allow)
    {
        var rule = cfg.Rules.FirstOrDefault(r => string.Equals(r.Trigger?.Trim(), trigger, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return false;

        if (rule.AllowWhenLastSeries is not null && rule.AllowWhenLastSeries.Count > 0)
            return false;

        var hasOnRelay = rule.Actions.Any(a => a is RelayActionConfig rac
                                              && string.Equals(rac.State?.Trim(), "On", StringComparison.OrdinalIgnoreCase));
        if (!hasOnRelay) return false;

        rule.AllowWhenLastSeries = allow.ToList();
        return true;
    }

    private static bool EnsureRule(AppConfig cfg, string trigger, bool enabledByDefault, ActionConfig action)
    {
        if (cfg.Rules.Any(r => string.Equals(r.Trigger?.Trim(), trigger, StringComparison.OrdinalIgnoreCase)))
            return false;

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = trigger,
            Enabled = enabledByDefault,
            Actions = new List<ActionConfig> { action }
        });

        return true;
    }

    private static bool EnsureProcessNames(MeetingSignalConfig cfg, IEnumerable<string> required)
    {
        cfg.ProcessNames ??= new List<string>();
        var changed = false;

        foreach (var name in required)
        {
            if (cfg.ProcessNames.Any(x => string.Equals(x?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                continue;
            cfg.ProcessNames.Add(name);
            changed = true;
        }

        return changed;
    }

    private static SoftwareSignalDefinition GetOrCreateNinjaSignal(AppConfig cfg)
    {
        cfg.SoftwareSignals.Signals ??= new List<SoftwareSignalDefinition>();

        var ninja = cfg.SoftwareSignals.Signals
            .FirstOrDefault(s => string.Equals(s.Name, "ninja-desktop", StringComparison.OrdinalIgnoreCase));

        if (ninja is null)
        {
            ninja = new SoftwareSignalDefinition
            {
                Name = "ninja-desktop",
                Enabled = false,
                RequireRdp = true,
                ProcessNames = new List<string>()
            };

            cfg.SoftwareSignals.Signals.Add(ninja);
        }

        return ninja;
    }

    public static AppConfig CreateDefault()
    {
        var cfg = new AppConfig
        {
            StartWithWindows = true,
            Serial = new SerialConfig
            {
                Port = null,
                AutoDetectCh340 = true,
                Address = 1,
                BaudRate = 9600,
                TimeoutMs = 500
            },
            Ui = new UiConfig
            {
                ShowBalloonOnActions = false
            },
            MeetingSignal = new MeetingSignalConfig
            {
                Enabled = false,
                PollSeconds = 2,
                ProcessNames = new List<string> { "Teams", "ms-teams", "MSTeams", "MicrosoftTeams", "Microsoft Teams", "Zoom" },
                Mode = "Process",
                CallWindowKeywords = new List<string> { "meeting", "call", "chiamata", "riunione" }
            },
            Update = new UpdateConfig
            {
                Enabled = true,
                CheckOnStartup = true,
                RepoOwner = "",
                RepoName = "",
                InstallerAssetName = "LcusRelay-Setup.exe",
                AutoInstallOnApproval = true
            }
        };

        cfg.SoftwareSignals = new SoftwareSignalsConfig
        {
            Enabled = true,
            EmitRdpSignal = true,
            PollSeconds = 2,
            Signals = new List<SoftwareSignalDefinition>
            {
                new SoftwareSignalDefinition
                {
                    Name = "ninja-desktop",
                    Enabled = false,
                    RequireRdp = true,
                    ProcessNames = new List<string> { "NinjaRMMAgentPatcher", "NinjaRMMAgent", "NinjaRemote", "NinjaRMM" }
                }
            }
        };
        
        cfg.MeetingReminders = new MeetingRemindersConfig
        {
            Enabled = false,
            PollSeconds = 15,
            Rules = new List<MeetingReminderRuleConfig>
            {
                new()
                {
                    Name = "default",
                    Enabled = false,
                    MeetingCron = "0 9 * * 1-5",
                    LeadMinutes = 10,
                    RepeatEveryMinutes = 2,
                    Blink = new BlinkActionConfig
                    {
                        Count = 3,
                        OnMs = 250,
                        OffMs = 250,
                        RestoreInitialState = true
                    }
                }
            }
        };

        // Default: spegni a logon/logoff (come hai richiesto)
        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "session:logon",
            Series = "session",
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "Off" } }
        });

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "session:logoff",
            Series = "session",
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "Off" } }
        });

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "session:lock",
            Series = "session",
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "Off" } }
        });

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "session:unlock",
            Series = "session",
            AllowWhenLastSeries = new List<string> { "session" },
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "On" } }
        });

        // Hotkey: toggle
        cfg.Hotkeys.Add(new HotkeyConfig
        {
            Name = "toggle",
            Modifiers = new List<string> { "Control", "Alt" },
            Key = "L",
            Series = "manual",
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "Toggle" } }
        });

        // Schedules esempio (disabilitati di default)
        cfg.Schedules.Add(new ScheduleConfig
        {
            Name = "weekdays-off-23",
            Enabled = false,
            Cron = "0 23 * * 1-5",
            Series = "schedule",
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "Off" } }
        });

        cfg.Schedules.Add(new ScheduleConfig
        {
            Name = "weekdays-on-0730",
            Enabled = false,
            Cron = "30 7 * * 1-5",
            Series = "schedule",
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "On" } }
        });

        // Meeting signal (se attivato, aggiungi queste regole)
        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "signal:meeting:on",
            Enabled = false,
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "Off" } }
        });

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "signal:meeting:off",
            Enabled = false,
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "On" } }
        });

        // RDP state signal (emesso dal watcher): utile per distinguere sessione locale vs remoto
        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "signal:rdp:on",
            Enabled = false,
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "Off" } }
        });

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "signal:rdp:off",
            Enabled = false,
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "On" } }
        });

        // Ninja Desktop (process + RDP)
        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "signal:ninja-desktop:on",
            Enabled = true,
            Actions = new List<ActionConfig>
            {
                new BlinkActionConfig
                {
                    Count = 3,
                    OnMs = 250,
                    OffMs = 250,
                    RestoreInitialState = true
                }
            }
        });

        cfg.Rules.Add(new RuleConfig
        {
            Trigger = "signal:ninja-desktop:off",
            Enabled = false,
            Actions = new List<ActionConfig> { new RelayActionConfig { State = "On" } }
        });

        return cfg;
    }
}
