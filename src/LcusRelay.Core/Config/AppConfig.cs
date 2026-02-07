
using System.Text.Json.Serialization;

namespace LcusRelay.Core.Config;

public sealed class AppConfig
{
    public SerialConfig Serial { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Regole: se Trigger == evento allora esegui Actions.</summary>
    public List<RuleConfig> Rules { get; set; } = new();

    /// <summary>Schedules cron indipendenti dalle regole "event".</summary>
    public List<ScheduleConfig> Schedules { get; set; } = new();

    /// <summary>Hotkey globali: quando premute generano trigger "hotkey:{Name}".</summary>
    public List<HotkeyConfig> Hotkeys { get; set; } = new();

    /// <summary>Segnale "meeting mode" basato su processi: genera trigger "signal:meeting:on/off".</summary>
    public MeetingSignalConfig MeetingSignal { get; set; } = new();

    /// <summary>Detect software/processi e stato RDP: genera trigger "signal:rdp:on/off" e "signal:{name}:on/off".</summary>
    public SoftwareSignalsConfig SoftwareSignals { get; set; } = new();
    
    /// <summary>
    /// Reminder pre-meeting basato su cron: da X minuti prima del meeting,
    /// esegue lampeggi ad intervalli configurabili e poi ripristina lo stato iniziale.
    /// </summary>
    public MeetingRemindersConfig MeetingReminders { get; set; } = new();

    /// <summary>Opzioni varie.</summary>
    public UiConfig Ui { get; set; } = new();

    /// <summary>Opzioni per eventi di sessione.</summary>
    public SessionConfig Session { get; set; } = new();

    /// <summary>Opzioni per aggiornamenti automatici.</summary>
    public UpdateConfig Update { get; set; } = new();
}

public sealed class SerialConfig
{
    /// <summary>Es: "COM5". Se vuoto e AutoDetectCh340=true prova a rilevare.</summary>
    public string? Port { get; set; } = null;

    public bool AutoDetectCh340 { get; set; } = true;

    /// <summary>Address del modulo (default 1).</summary>
    public int Address { get; set; } = 1;

    public int BaudRate { get; set; } = 9600;

    public int TimeoutMs { get; set; } = 500;
}

public sealed class UiConfig
{
    public bool ShowBalloonOnActions { get; set; } = false;
}

public sealed class SessionConfig
{
    /// <summary>
    /// Se true, ignora session:logon e session:unlock quando la sessione è RDP.
    /// </summary>
    public bool SuppressLogonUnlockWhenRdp { get; set; } = true;

    /// <summary>
    /// Se true, ignora session:lock quando la sessione è RDP.
    /// </summary>
    public bool SuppressLockWhenRdp { get; set; } = true;

    /// <summary>
    /// Se true, se la lampada era OFF prima del lock allora ignora session:unlock.
    /// </summary>
    public bool KeepOffOnUnlockIfWasOff { get; set; } = true;
}

public sealed class UpdateConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Check update on app startup.</summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>Owner of the GitHub repo.</summary>
    public string RepoOwner { get; set; } = "GabrieleAngeli";

    /// <summary>Name of the GitHub repo.</summary>
    public string RepoName { get; set; } = "LcusRelay";

    /// <summary>Installer asset name in the GitHub Release.</summary>
    public string InstallerAssetName { get; set; } = "LcusRelay-Setup.exe";

    /// <summary>If true, after user approval the installer is downloaded and launched automatically.</summary>
    public bool AutoInstallOnApproval { get; set; } = true;
}

public sealed class MeetingSignalConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>Nome processi senza estensione, es. "Teams", "Zoom", "ms-teams".</summary>
    public List<string> ProcessNames { get; set; } = new() { "Teams", "ms-teams", "Zoom" };

    /// <summary>Polling interval in secondi.</summary>
    public int PollSeconds { get; set; } = 2;

    /// <summary>
    /// Modalità rilevamento meeting: "Process" o "TeamsCall".
    /// </summary>
    public string Mode { get; set; } = "Process";

    /// <summary>
    /// Parole chiave per finestra/meeting (usate solo in modalità TeamsCall).
    /// </summary>
    public List<string> CallWindowKeywords { get; set; } = new()
    {
        "meeting",
        "call",
        "chiamata",
        "riunione"
    };
}
public sealed class SoftwareSignalsConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>Se true, emette "signal:rdp:on/off" quando cambia lo stato della sessione RDP.</summary>
    public bool EmitRdpSignal { get; set; } = true;

    /// <summary>Polling interval in secondi.</summary>
    public int PollSeconds { get; set; } = 2;

    /// <summary>Elenco segnali basati su processi: per ciascuno genera "signal:{name}:on/off".</summary>
    public List<SoftwareSignalDefinition> Signals { get; set; } = new();
}

public sealed class SoftwareSignalDefinition
{
    public string Name { get; set; } = "signal";
    public bool Enabled { get; set; } = false;

    /// <summary>Se true, il segnale è ON solo quando la sessione è in RDP.</summary>
    public bool RequireRdp { get; set; } = false;

    /// <summary>Nome processi senza estensione, es. "Teams", "Zoom".</summary>
    public List<string> ProcessNames { get; set; } = new();
}

public sealed class MeetingRemindersConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>Polling interval in secondi.</summary>
    public int PollSeconds { get; set; } = 15;

    public List<MeetingReminderRuleConfig> Rules { get; set; } = new();
}

public sealed class MeetingReminderRuleConfig
{
    public string Name { get; set; } = "default";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cron dell'inizio meeting (5 campi).
    /// Esempio: "0 9 * * 1-5" => meeting alle 09:00 dal lunedì al venerdì.
    /// </summary>
    public string MeetingCron { get; set; } = "0 9 * * 1-5";

    /// <summary>Quanti minuti prima del meeting attivare i reminder.</summary>
    public int LeadMinutes { get; set; } = 10;

    /// <summary>Ogni quanti minuti ripetere il reminder nella finestra pre-meeting.</summary>
    public int RepeatEveryMinutes { get; set; } = 2;

    /// <summary>Configurazione blink/lampo.</summary>
    public BlinkActionConfig Blink { get; set; } = new();
}


public sealed class RuleConfig
{
    /// <summary>Es: "session:logon", "session:logoff", "session:lock", "session:unlock", "hotkey:toggle"</summary>
    public string Trigger { get; set; } = "";

    /// <summary>
    /// Serie/logica d'origine (es. "manual", "session", "schedule"). Usata per guardie di serie.
    /// </summary>
    public string? Series { get; set; }

    /// <summary>
    /// Se valorizzato, la regola scatta solo se l'ultimo cambio stato è avvenuto da una delle serie elencate.
    /// </summary>
    public List<string>? AllowWhenLastSeries { get; set; }

    public bool Enabled { get; set; } = true;

    public List<ActionConfig> Actions { get; set; } = new();
}

public sealed class ScheduleConfig
{
    /// <summary>Nome user-friendly.</summary>
    public string Name { get; set; } = "schedule";

    public bool Enabled { get; set; } = true;

    /// <summary>Cron in formato 5 campi: "min hour day month dayOfWeek". Esempio: "0 23 * * 1-5".</summary>
    public string Cron { get; set; } = "0 23 * * 1-5";

    /// <summary>Se true, interpreta il cron in UTC.</summary>
    public bool UseUtc { get; set; } = false;

    /// <summary>Serie/logica d'origine (es. "schedule").</summary>
    public string? Series { get; set; }

    /// <summary>
    /// Se valorizzato, la regola scatta solo se l'ultimo cambio stato è avvenuto da una delle serie elencate.
    /// </summary>
    public List<string>? AllowWhenLastSeries { get; set; }

    public List<ActionConfig> Actions { get; set; } = new();
}

public sealed class HotkeyConfig
{
    public string Name { get; set; } = "toggle";

    /// <summary>Es: ["Control","Alt"]</summary>
    public List<string> Modifiers { get; set; } = new() { "Control", "Alt" };

    /// <summary>Es: "L"</summary>
    public string Key { get; set; } = "L";

    /// <summary>Serie/logica d'origine (es. "manual").</summary>
    public string? Series { get; set; }

    /// <summary>
    /// Se valorizzato, la regola scatta solo se l'ultimo cambio stato è avvenuto da una delle serie elencate.
    /// </summary>
    public List<string>? AllowWhenLastSeries { get; set; }

    public List<ActionConfig> Actions { get; set; } = new();
}

/// <summary>
/// Azioni polimorfiche serializzabili in JSON.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RelayActionConfig), typeDiscriminator: "relay")]

[JsonDerivedType(typeof(BlinkActionConfig), typeDiscriminator: "blink")]
[JsonDerivedType(typeof(RunProcessActionConfig), typeDiscriminator: "run")]
[JsonDerivedType(typeof(WebhookActionConfig), typeDiscriminator: "webhook")]
[JsonDerivedType(typeof(DelayActionConfig), typeDiscriminator: "delay")]
public abstract class ActionConfig
{
}

public sealed class RelayActionConfig : ActionConfig
{
    /// <summary>"On" | "Off" | "Toggle"</summary>
    public string State { get; set; } = "Toggle";
}

public sealed class RunProcessActionConfig : ActionConfig
{
    public string FileName { get; set; } = "";
    public string? Arguments { get; set; }
    public bool HiddenWindow { get; set; } = true;
}

public sealed class BlinkActionConfig : ActionConfig
{
    /// <summary>Numero lampeggi.</summary>
    public int Count { get; set; } = 3;

    /// <summary>Durata stato invertito (ms).</summary>
    public int OnMs { get; set; } = 1000;

    /// <summary>Durata stato base tra un lampeggio e il successivo (ms).</summary>
    public int OffMs { get; set; } = 1000;

    /// <summary>
    /// Sequenza opzionale (ms) per lo stato invertito. Se presente, prevale su OnMs.
    /// Se più corta di Count, viene riusata in loop.
    /// </summary>
    public List<int>? OnMsSequence { get; set; }

    /// <summary>
    /// Sequenza opzionale (ms) per lo stato base tra lampeggi. Se presente, prevale su OffMs.
    /// Se più corta di Count, viene riusata in loop.
    /// </summary>
    public List<int>? OffMsSequence { get; set; }

    /// <summary>Se true, al termine ripristina lo stato iniziale del relay.</summary>
    public bool RestoreInitialState { get; set; } = true;
}

public sealed class WebhookActionConfig : ActionConfig
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "POST";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
}

public sealed class DelayActionConfig : ActionConfig
{
    public int Milliseconds { get; set; } = 1000;
}
