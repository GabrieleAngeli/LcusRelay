using Cronos;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;
using ThreadingTimer = System.Threading.Timer;

namespace LcusRelay.Tray.Services;

/// <summary>
/// Reminder pre-meeting:
/// - dato un cron di inizio meeting
/// - apre una finestra [meetingStart - lead, meetingStart)
/// - nella finestra emette callback ogni N minuti (repeat)
/// </summary>
public sealed class MeetingReminderWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly MeetingRemindersConfig _cfg;
    private readonly Func<MeetingReminderRuleConfig, Task> _onReminder;

    private readonly List<RuleState> _rules = new();
    private readonly Dictionary<string, DateTimeOffset> _firedSlots = new(StringComparer.OrdinalIgnoreCase);

    private ThreadingTimer? _timer;

    public MeetingReminderWatcher(
        ILogger log,
        MeetingRemindersConfig cfg,
        Func<MeetingReminderRuleConfig, Task> onReminder)
    {
        _log = log;
        _cfg = cfg;
        _onReminder = onReminder;
    }

    public void Start()
    {
        if (!_cfg.Enabled)
        {
            _log.LogInformation("MeetingReminderWatcher disabled.");
            return;
        }

        _rules.Clear();

        foreach (var rule in (_cfg.Rules ?? new List<MeetingReminderRuleConfig>()).Where(r => r.Enabled))
        {
            try
            {
                var expr = CronExpression.Parse(rule.MeetingCron, CronFormat.Standard);
                _rules.Add(new RuleState(rule, expr));
                _log.LogInformation("MeetingReminder enabled: {name} cron={cron}", rule.Name, rule.MeetingCron);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "MeetingReminder cron non valido per {name}: {cron}", rule.Name, rule.MeetingCron);
            }
        }

        if (_rules.Count == 0)
        {
            _log.LogInformation("MeetingReminderWatcher: nessuna regola valida attiva.");
            return;
        }

        var seconds = Math.Clamp(_cfg.PollSeconds, 5, 60);
        _timer = new ThreadingTimer(_ => TickSafe(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(seconds));

        _log.LogInformation("MeetingReminderWatcher started (poll {sec}s, rules={count}).", seconds, _rules.Count);
    }

    private void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { _log.LogError(ex, "Errore MeetingReminderWatcher tick"); }
    }

    private void Tick()
    {
        if (_rules.Count == 0) return;

        var now = DateTimeOffset.Now;
        var tz = TimeZoneInfo.Local;

        foreach (var st in _rules)
        {
            var leadMin = Math.Clamp(st.Rule.LeadMinutes, 1, 24 * 60);
            var repeatMin = Math.Clamp(st.Rule.RepeatEveryMinutes, 1, 24 * 60);

            // Prende l'occorrenza "vicina" al pre-window corrente.
            var lookupBase = now.AddMinutes(-leadMin);
            var meetingStart = st.Expression.GetNextOccurrence(lookupBase, tz);
            if (meetingStart is null) continue;

            var windowStart = meetingStart.Value.AddMinutes(-leadMin);
            var windowEnd = meetingStart.Value;

            // Fuori finestra pre-meeting
            if (now < windowStart || now >= windowEnd)
                continue;

            var elapsed = now - windowStart;
            var slot = (int)Math.Floor(elapsed.TotalMinutes / repeatMin);
            if (slot < 0) continue;

            var key = $"{st.Rule.Name}|{meetingStart.Value:O}|{slot}";
            if (_firedSlots.ContainsKey(key))
                continue;

            _firedSlots[key] = now;
            CleanupFired(now);

            _log.LogInformation(
                "Meeting reminder fired: {name} slot={slot} meetingStart={meetingStart}",
                st.Rule.Name,
                slot,
                meetingStart.Value);

            _ = Fire(st.Rule);
        }
    }

    private async Task Fire(MeetingReminderRuleConfig rule)
    {
        try
        {
            await _onReminder(rule).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore meeting reminder fire {name}", rule.Name);
        }
    }

    private void CleanupFired(DateTimeOffset now)
    {
        if (_firedSlots.Count <= 300) return;

        var threshold = now.AddDays(-2);
        var oldKeys = _firedSlots
            .Where(kv => kv.Value < threshold)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var k in oldKeys)
            _firedSlots.Remove(k);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _rules.Clear();
        _firedSlots.Clear();
        _log.LogInformation("MeetingReminderWatcher disposed.");
    }

    private sealed record RuleState(MeetingReminderRuleConfig Rule, CronExpression Expression);
}
