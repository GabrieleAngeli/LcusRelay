
using System.Diagnostics;
using System.Threading;
using LcusRelay.Core.Actions;
using LcusRelay.Core.Automation;
using LcusRelay.Core.Config;
using LcusRelay.Core.Relay;
using LcusRelay.Tray.Forms;
using LcusRelay.Tray.Logging;
using LcusRelay.Tray.Services;
using LcusRelay.Tray.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LcusRelay.Tray;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notify;
    private readonly IServiceProvider _services;
    private readonly ILogger _log;
    private readonly SimpleFileLoggerProvider _fileLoggerProvider;
    private readonly SynchronizationContext? _uiContext;
    private readonly GitHubUpdateService _updateService = new();

    private AppConfig _config;
    private AutomationEngine _engine;

    private SessionWatcher? _sessionWatcher;
    private HotkeyWatcher? _hotkeyWatcher;
    private ScheduleWatcher? _scheduleWatcher;
    private MeetingProcessWatcher? _meetingWatcher;
    private SoftwareSignalsWatcher? _softwareSignalsWatcher;
    private MeetingReminderWatcher? _meetingReminderWatcher;

    private readonly SemaphoreSlim _blinkSemaphore = new(1, 1);
    private bool? _wasOffBeforeLock;

    public TrayAppContext()
    {
        _services = BuildServices(out _fileLoggerProvider);
        _log = _services.GetRequiredService<ILoggerFactory>().CreateLogger("LcusRelay.Tray");
        _log.LogInformation("Avvio applicazione. Log file: {path}", _fileLoggerProvider.FilePath);
        _uiContext = SynchronizationContext.Current;

        RegisterGlobalExceptionHandlers();

        _config = ConfigStore.LoadOrCreateDefault(_log);
        ApplyStartupSetting(_config);

        _engine = _services.GetRequiredService<AutomationEngine>();
        ReloadEngine(_config);

        _notify = new NotifyIcon
        {
            Text = "LcusRelay",
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notify.DoubleClick += (_, __) => OpenSettings();

        StartWatchers(_config);

        // Auto-azione (opzionale): al primo avvio in sessione puoi decidere di spegnere subito
        // fire session:logon (utile se avvii via Run key)
        _ = FireSafeAsync("session:logon");
        _ = FireSafeAsync("system:startup");

        // Update check (non-blocking)
        _ = CheckForUpdatesAsync();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
        {
            _log.LogError(e.Exception, "ThreadException non gestita.");
            ShowBalloon("LcusRelay", $"Errore: {e.Exception.Message}", ToolTipIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                _log.LogError(ex, "UnhandledException (AppDomain).");
            }
            else
            {
                _log.LogError("UnhandledException (AppDomain) non-Exception: {obj}", e.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log.LogError(e.Exception, "UnobservedTaskException.");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, __) =>
        {
            try
            {
                if (_notify is not null)
                {
                    _notify.Visible = false;
                    _notify.Dispose();
                }
            }
            catch { }
        };
    }

    private IServiceProvider BuildServices(out SimpleFileLoggerProvider fileLoggerProvider)
    {
        var sc = new ServiceCollection();

        var logDir = ConfigStore.GetAppDataDir();
        var loggerProvider = new SimpleFileLoggerProvider(Path.Combine(logDir, "lcusrelay.log"), mirrorConsole: true);

        sc.AddLogging(b =>
        {
            b.ClearProviders();
            b.SetMinimumLevel(LogLevel.Information);
            b.AddProvider(loggerProvider);
        });

        fileLoggerProvider = loggerProvider;


        // Services
        sc.AddSingleton<AutomationEngine>();

        // Relay controller: viene creato "on demand" con settings aggiornati alla config corrente.
        // Nota: lo re-installeremo ad ogni reload config.
        sc.AddSingleton<RelayControllerHolder>();

        sc.AddSingleton<IRelayController>(sp => sp.GetRequiredService<RelayControllerHolder>().Current);
        sc.AddSingleton<IRelayStateStore, RelayStateStore>();

        return sc.BuildServiceProvider();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.RenderMode = ToolStripRenderMode.System;

        var version = typeof(TrayAppContext).Assembly.GetName().Version?.ToString() ?? "unknown";
        var versionItem = new ToolStripMenuItem($"Version {version}") { Enabled = false };
        menu.Items.Add(versionItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Turn On", null, async (_, __) => await RelaySetSafeAsync(true));
        menu.Items.Add("Turn Off", null, async (_, __) => await RelaySetSafeAsync(false));
        menu.Items.Add("Toggle", null, async (_, __) => await RelayToggleSafeAsync());
        menu.Items.Add("Test Blink", null, async (_, __) => await BlinkSafeAsync(new BlinkActionConfig()));

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Settings…", null, (_, __) => OpenSettings());
        menu.Items.Add("Edit config.json", null, (_, __) =>
        {
            if (!ConfigStore.TryOpenConfigInEditor(_log, out var error))
            {
                ShowBalloon("LcusRelay", $"Impossibile aprire config.json: {error}", ToolTipIcon.Error);
            }
        });
        menu.Items.Add("Reload config", null, (_, __) => ReloadFromDisk());

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, __) => Exit());

        return menu;
    }

    private void StartWatchers(AppConfig cfg)
    {
        StopWatchers();

        _sessionWatcher = new SessionWatcher(_log, cfg.Session, async ev => await FireSafeAsync(ev));
        _sessionWatcher.Start();

        _hotkeyWatcher = new HotkeyWatcher(_log, cfg.Hotkeys, async name => await FireSafeAsync($"hotkey:{name}"));
        _hotkeyWatcher.Start();

        _scheduleWatcher = new ScheduleWatcher(_log, cfg.Schedules, async name => await FireSafeAsync($"schedule:{name}"));
        _scheduleWatcher.Start();

        _meetingWatcher = new MeetingProcessWatcher(_log, cfg.MeetingSignal, async on => await FireSafeAsync(on ? "signal:meeting:on" : "signal:meeting:off"));
        _meetingWatcher.Start();

        _softwareSignalsWatcher = new SoftwareSignalsWatcher(_log, cfg.SoftwareSignals, async trig => await FireSafeAsync(trig));
        _softwareSignalsWatcher.Start();        

        _meetingReminderWatcher = new MeetingReminderWatcher(_log, cfg.MeetingReminders, async rule => await FireMeetingReminderAsync(rule));
        _meetingReminderWatcher.Start();
    }

    private void StopWatchers()
    {
        _sessionWatcher?.Dispose();
        _hotkeyWatcher?.Dispose();
        _scheduleWatcher?.Dispose();
        _meetingWatcher?.Dispose();
        _softwareSignalsWatcher?.Dispose();
        _meetingReminderWatcher?.Dispose();

        _sessionWatcher = null;
        _hotkeyWatcher = null;
        _scheduleWatcher = null;
        _meetingWatcher = null;
        _softwareSignalsWatcher = null;
        _meetingReminderWatcher = null;
    }

    private void ReloadEngine(AppConfig cfg)
    {
        // Aggiorna relay controller
        var holder = _services.GetRequiredService<RelayControllerHolder>();
        holder.Replace(CreateRelayFromConfig(cfg));

        // Compila regole: unisce Rules + Hotkeys + Schedules
        var rules = new List<RuleConfig>();
        rules.AddRange(cfg.Rules);

        foreach (var hk in cfg.Hotkeys.Where(h => h.Actions.Count > 0))
        {
            rules.Add(new RuleConfig
            {
                Trigger = $"hotkey:{hk.Name}",
                Enabled = true,
                Series = string.IsNullOrWhiteSpace(hk.Series) ? "manual" : hk.Series,
                AllowWhenLastSeries = hk.AllowWhenLastSeries,
                Actions = hk.Actions
            });
        }

        foreach (var sch in cfg.Schedules.Where(s => s.Actions.Count > 0))
        {
            rules.Add(new RuleConfig
            {
                Trigger = $"schedule:{sch.Name}",
                Enabled = true,
                Series = string.IsNullOrWhiteSpace(sch.Series) ? "schedule" : sch.Series,
                AllowWhenLastSeries = sch.AllowWhenLastSeries,
                Actions = sch.Actions
            });
        }

        _engine.LoadRules(rules);
    }

    private IRelayController CreateRelayFromConfig(AppConfig cfg)
    {
        var port = cfg.Serial.Port;

        if (string.IsNullOrWhiteSpace(port) && cfg.Serial.AutoDetectCh340)
        {
            port = PortDetection.TryFindCh340ComPort(_log);
            if (!string.IsNullOrWhiteSpace(port))
            {
                cfg.Serial.Port = port;
                ConfigStore.Save(_log, cfg); // persistiamo auto-detect
            }
        }

        var settings = new SerialRelaySettings
        {
            PortName = port?.Trim() ?? "",
            Address = cfg.Serial.Address,
            BaudRate = cfg.Serial.BaudRate,
            TimeoutMs = cfg.Serial.TimeoutMs
        };

        var relayLogger = _services.GetRequiredService<ILoggerFactory>().CreateLogger<LcusSerialRelayController>();
        return new LcusSerialRelayController(settings, relayLogger);
    }

    private void ApplyStartupSetting(AppConfig cfg)
    {
        try
        {
            if (cfg.StartWithWindows)
                StartupRegistry.EnableForCurrentUser(_log);
            else
                StartupRegistry.DisableForCurrentUser(_log);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Impossibile applicare StartWithWindows.");
        }
    }

    private async Task FireSafeAsync(string trigger)
    {
        try
        {
            _log.LogInformation("Trigger: {trigger}", trigger);

            if (string.Equals(trigger, "session:lock", StringComparison.OrdinalIgnoreCase))
            {
                var relay = _services.GetRequiredService<IRelayController>();
                _wasOffBeforeLock = relay.LastKnownState == false;
                _log.LogInformation("Session lock: lampada era {state}", _wasOffBeforeLock == true ? "OFF" : "ON/unknown");
            }

            if (string.Equals(trigger, "session:unlock", StringComparison.OrdinalIgnoreCase))
            {
                if (_config.Session.KeepOffOnUnlockIfWasOff && _wasOffBeforeLock == true)
                {
                    _log.LogInformation("Session unlock ignorato: lampada era OFF prima del lock.");
                    _wasOffBeforeLock = null;
                    return;
                }

                _wasOffBeforeLock = null;
            }

            await _engine.FireAsync(new TriggerEvent(trigger)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore eseguendo trigger {trigger}", trigger);
            ShowBalloon("LcusRelay", $"Errore: {ex.Message}", ToolTipIcon.Error);
        }
    }
    
    private async Task FireMeetingReminderAsync(MeetingReminderRuleConfig rule)
    {
        var blink = rule.Blink ?? new BlinkActionConfig();
        await BlinkSafeAsync(blink, $"meeting-reminder:{rule.Name}").ConfigureAwait(false);
    }

    private async Task RelaySetSafeAsync(bool on)
    {
        try
        {
            var relay = _services.GetRequiredService<IRelayController>();
            await relay.SetAsync(on).ConfigureAwait(false);
            var stateStore = _services.GetRequiredService<IRelayStateStore>();
            stateStore.RecordRelayChange(on, on ? "ui:tray:on" : "ui:tray:off", "manual");
            _log.LogInformation("Relay set manuale: {state}", on ? "On" : "Off");
            ShowBalloon("LcusRelay", on ? "ON" : "OFF", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore relay set");
            ShowBalloon("LcusRelay", $"Relay error: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private async Task RelayToggleSafeAsync()
    {
        try
        {
            var relay = _services.GetRequiredService<IRelayController>();
            var current = relay.LastKnownState ?? false;
            var next = !current;
            await relay.SetAsync(next).ConfigureAwait(false);
            var stateStore = _services.GetRequiredService<IRelayStateStore>();
            stateStore.RecordRelayChange(next, "ui:tray:toggle", "manual");
            _log.LogInformation("Relay toggle manuale: da {from} a {to}", current ? "On" : "Off", !current ? "On" : "Off");
            ShowBalloon("LcusRelay", !current ? "ON" : "OFF", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore relay toggle");
            ShowBalloon("LcusRelay", $"Relay error: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private async Task BlinkSafeAsync(BlinkActionConfig cfg, string source = "blink")
    {
        // evita sovrapposizioni tra reminder/hotkey/test manuale
        if (!await _blinkSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            _log.LogInformation("Blink skipped (busy).");
            return;
        }

        try
        {
            var action = new BlinkAction(cfg);
            await action.ExecuteAsync(new ActionContext
            {
                Trigger = source,
                Services = _services
            }, CancellationToken.None).ConfigureAwait(false);

            ShowBalloon("LcusRelay", "Blink eseguito", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore blink");
            ShowBalloon("LcusRelay", $"Blink error: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _blinkSemaphore.Release();
        }
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        // evita spam
        _notify.BalloonTipTitle = title;
        _notify.BalloonTipText = text;
        _notify.BalloonTipIcon = icon;
        _notify.ShowBalloonTip(2500);
    }

    private void ReloadFromDisk()
    {
        try
        {
            var cfg = ConfigStore.LoadOrCreateDefault(_log);
            _config = cfg;
            ApplyStartupSetting(_config);
            ReloadEngine(_config);
            StartWatchers(_config);
            ShowBalloon("LcusRelay", "Config ricaricata", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore reload config");
            ShowBalloon("LcusRelay", $"Reload error: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_log, _config, async updated =>
        {
            _config = updated;
            ApplyStartupSetting(_config);
            ReloadEngine(_config);
            StartWatchers(_config);
            await Task.CompletedTask;
        });

        form.ShowDialog();
    }

    private void Exit()
    {
        _notify.Visible = false;
        _notify.Dispose();
        StopWatchers();
        _fileLoggerProvider.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notify.Visible = false;
            _notify.Dispose();
            StopWatchers();
            _fileLoggerProvider.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var cfg = _config.Update;
            if (!cfg.Enabled || !cfg.CheckOnStartup)
                return;

            var currentVersion = typeof(TrayAppContext).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            var info = await _updateService.CheckForUpdateAsync(cfg, currentVersion, _log, CancellationToken.None).ConfigureAwait(false);
            if (info is null)
                return;

            ShowBalloon("LcusRelay", $"Update available: v{info.Version}", ToolTipIcon.Info);

            var approved = await PromptUpdateAsync(info).ConfigureAwait(false);
            if (!approved)
                return;

            if (!cfg.AutoInstallOnApproval)
                return;

            var installerPath = await _updateService.DownloadInstallerAsync(info, _log, CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(installerPath))
                return;

            var started = TryStartInstaller(installerPath);
            if (started)
            {
                Exit();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed.");
        }
    }

    private Task<bool> PromptUpdateAsync(UpdateInfo info)
    {
        var tcs = new TaskCompletionSource<bool>();

        void ShowPrompt()
        {
            var result = MessageBox.Show(
                $"A new version is available: v{info.Version}\n\nDo you want to download and install it now?",
                "LcusRelay Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );
            tcs.TrySetResult(result == DialogResult.Yes);
        }

        if (_uiContext is not null)
            _uiContext.Post(_ => ShowPrompt(), null);
        else
            ShowPrompt();

        return tcs.Task;
    }

    private bool TryStartInstaller(string installerPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /NORESTART",
                UseShellExecute = true
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to start installer.");
            ShowBalloon("LcusRelay", "Update failed to start installer.", ToolTipIcon.Warning);
            return false;
        }
    }
}

/// <summary>
/// Holder per sostituire a runtime l'IRelayController quando ricarichi la config.
/// </summary>
public sealed class RelayControllerHolder
{
    private IRelayController _current = new LcusSerialRelayController(new SerialRelaySettings { PortName = "", Address = 1 });

    public IRelayController Current => _current;

    public void Replace(IRelayController next) => _current = next;
}
