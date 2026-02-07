using Microsoft.Extensions.Logging;

using LcusRelay.Core.Config;

namespace LcusRelay.Core.Automation;

/// <summary>
/// Motore semplice: matcha Trigger (stringa) e lancia una lista di azioni.
/// </summary>
public sealed class AutomationEngine
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AutomationEngine> _log;
    private readonly Dictionary<string, List<CompiledRule>> _rules = new(StringComparer.OrdinalIgnoreCase);

    public AutomationEngine(IServiceProvider services, ILogger<AutomationEngine> log)
    {
        _services = services;
        _log = log;
    }

    public void LoadRules(IEnumerable<RuleConfig> rules)
    {
        _rules.Clear();
        var enabledRules = rules.Where(r => r.Enabled).ToList();
        _log.LogInformation("Caricamento regole: {count}", enabledRules.Count);

        foreach (var rule in enabledRules)
        {
            var compiled = new List<IAction>();
            foreach (var actionCfg in rule.Actions)
            {
                compiled.Add(ActionFactory.Create(actionCfg, _services));
            }

            if (!_rules.TryGetValue(rule.Trigger.Trim(), out var list))
            {
                list = new List<CompiledRule>();
                _rules[rule.Trigger.Trim()] = list;
            }

            list.Add(new CompiledRule(
                rule.Trigger.Trim(),
                rule.Series?.Trim(),
                NormalizeAllowList(rule.AllowWhenLastSeries),
                compiled
            ));

            _log.LogInformation("Regola caricata: trigger={trigger}, actions={actions}, series={series}",
                rule.Trigger.Trim(),
                compiled.Count,
                string.IsNullOrWhiteSpace(rule.Series) ? "-" : rule.Series.Trim());
        }
    }

    public async Task FireAsync(TriggerEvent ev, CancellationToken cancellationToken = default)
    {
        if (!_rules.TryGetValue(ev.Name.Trim(), out var rules))
        {
            _log.LogInformation("Nessuna regola trovata per trigger {trigger}", ev.Name.Trim());
            return;
        }

        var stateStore = (IRelayStateStore?)_services.GetService(typeof(IRelayStateStore));
        var lastSeries = stateStore?.Snapshot.LastSeries;

        _log.LogInformation("Esecuzione trigger {trigger} su {count} regola/e", ev.Name.Trim(), rules.Count);

        foreach (var rule in rules)
        {
            if (!IsAllowedBySeries(rule.AllowWhenLastSeries, lastSeries))
            {
                _log.LogInformation("Regola ignorata per serie: trigger={trigger}, series={series}, lastSeries={lastSeries}",
                    rule.Trigger,
                    string.IsNullOrWhiteSpace(rule.Series) ? "-" : rule.Series,
                    string.IsNullOrWhiteSpace(lastSeries) ? "-" : lastSeries);
                continue;
            }

            var data = MergeData(ev.Data, rule.Series);
            foreach (var action in rule.Actions)
            {
                await action.ExecuteAsync(new ActionContext
                {
                    Trigger = ev.Name,
                    Services = _services,
                    Data = data
                }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> MergeData(IReadOnlyDictionary<string, string> data, string? series)
    {
        if (string.IsNullOrWhiteSpace(series))
            return data;

        if (data.TryGetValue("series", out _))
            return data;

        var merged = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase)
        {
            ["series"] = series.Trim()
        };
        return merged;
    }

    private static HashSet<string>? NormalizeAllowList(List<string>? list)
    {
        if (list is null || list.Count == 0) return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in list)
        {
            var v = (raw ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(v)) set.Add(v);
        }

        return set.Count > 0 ? set : null;
    }

    private static bool IsAllowedBySeries(HashSet<string>? allowList, string? lastSeries)
    {
        if (allowList is null || allowList.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(lastSeries)) return true; // unknown => allow (non blocchiamo il primo evento)
        return allowList.Contains(lastSeries.Trim());
    }

    private sealed record CompiledRule(
        string Trigger,
        string? Series,
        HashSet<string>? AllowWhenLastSeries,
        List<IAction> Actions
    );
}
