
using Cronos;
using LcusRelay.Core.Config;
using Microsoft.Extensions.Logging;
using ThreadingTimer = System.Threading.Timer;

namespace LcusRelay.Tray.Services;

public sealed class ScheduleWatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly List<ScheduleConfig> _schedules;
    private readonly Func<string, Task> _onSchedule;
    private ThreadingTimer? _timer;

    private readonly Dictionary<string, CronState> _state = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastTick;

    public ScheduleWatcher(ILogger log, List<ScheduleConfig> schedules, Func<string, Task> onSchedule)
    {
        _log = log;
        _schedules = schedules;
        _onSchedule = onSchedule;
    }

    public void Start()
    {
        _state.Clear();
        _lastTick = null;

        foreach (var s in _schedules.Where(x => x.Enabled))
        {
            try
            {
                var expr = CronExpression.Parse(s.Cron, CronFormat.Standard);
                _state[s.Name] = new CronState(s.Name, s.Cron, expr, s.UseUtc);
                _log.LogInformation("Schedule enabled: {name} cron={cron} tz={tz}", s.Name, s.Cron, s.UseUtc ? "UTC" : "Local");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Cron non valido per schedule {name}: {cron}", s.Name, s.Cron);
            }
        }

        // check ogni 10s
        _timer = new ThreadingTimer(_ => TickSafe(), null, dueTime: TimeSpan.FromSeconds(2), period: TimeSpan.FromSeconds(10));
        _log.LogInformation("ScheduleWatcher started.");
    }

    private void TickSafe()
    {
        try { Tick(); }
        catch (Exception ex) { _log.LogError(ex, "Errore ScheduleWatcher tick"); }
    }

    private void Tick()
    {
        if (_state.Count == 0) return;

        var now = DateTimeOffset.Now;
        var lastTick = _lastTick ?? now.AddSeconds(-10);
        _lastTick = now;

        foreach (var st in _state.Values)
        {
            var tz = st.UseUtc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
            var baseNow = st.UseUtc ? now.ToUniversalTime() : now;
            var baseLast = st.UseUtc ? lastTick.ToUniversalTime() : lastTick;

            var next = st.Expression.GetNextOccurrence(baseLast, tz);
            if (next is null) continue;

            if (next <= baseNow)
            {
                // evita doppio firing troppo ravvicinato
                if (st.LastFiredAt is null || (baseNow - st.LastFiredAt.Value) > TimeSpan.FromSeconds(30))
                {
                    st.LastFiredAt = baseNow;
                    _ = FireSchedule(st.Name);
                }
            }
        }
    }

    private async Task FireSchedule(string name)
    {
        try
        {
            await _onSchedule(name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore schedule {name}", name);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _state.Clear();
        _lastTick = null;
        _log.LogInformation("ScheduleWatcher disposed.");
    }

    private sealed class CronState
    {
        public CronState(string name, string cron, CronExpression expression, bool useUtc)
        {
            Name = name;
            Cron = cron;
            Expression = expression;
            UseUtc = useUtc;
        }

        public string Name { get; }
        public string Cron { get; }
        public CronExpression Expression { get; }
        public bool UseUtc { get; }

        public DateTimeOffset? LastFiredAt { get; set; }
    }
}
