using Microsoft.Extensions.Logging;

using LcusRelay.Core.Automation;
using LcusRelay.Core.Config;
using LcusRelay.Core.Relay;

namespace LcusRelay.Core.Actions;

public sealed class RelayAction : IAction
{
    private readonly RelayActionConfig _cfg;

    public RelayAction(RelayActionConfig cfg)
    {
        _cfg = cfg;
    }

    public async Task ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var relay = (IRelayController?)context.Services.GetService(typeof(IRelayController))
                    ?? throw new InvalidOperationException("IRelayController non registrato nei Services.");

        var logger = (ILogger<RelayAction>?)context.Services.GetService(typeof(ILogger<RelayAction>));
        var stateStore = (IRelayStateStore?)context.Services.GetService(typeof(IRelayStateStore));
        var state = (_cfg.State ?? "Toggle").Trim();
        logger?.LogInformation("RelayAction eseguita da trigger {trigger}: state={state}", context.Trigger, state);

        if (state.Equals("Toggle", StringComparison.OrdinalIgnoreCase))
        {
            var current = relay.LastKnownState ?? false;
            var next = !current;
            await relay.SetAsync(next, cancellationToken).ConfigureAwait(false);
            stateStore?.RecordRelayChange(next, context.Trigger, GetSeries(context));
            logger?.LogInformation("RelayAction toggle completata: {to}", !current ? "On" : "Off");
            return;
        }

        if (state.Equals("On", StringComparison.OrdinalIgnoreCase))
        {
            await relay.SetAsync(true, cancellationToken).ConfigureAwait(false);
            stateStore?.RecordRelayChange(true, context.Trigger, GetSeries(context));
            logger?.LogInformation("RelayAction completata: On");
            return;
        }

        if (state.Equals("Off", StringComparison.OrdinalIgnoreCase))
        {
            await relay.SetAsync(false, cancellationToken).ConfigureAwait(false);
            stateStore?.RecordRelayChange(false, context.Trigger, GetSeries(context));
            logger?.LogInformation("RelayAction completata: Off");
            return;
        }

        throw new NotSupportedException($"RelayAction.State non supportato: '{_cfg.State}'. Usa On/Off/Toggle.");
    }

    private static string? GetSeries(ActionContext context)
    {
        if (context.Data.TryGetValue("series", out var series))
        {
            var v = (series ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }

        return null;
    }
}
