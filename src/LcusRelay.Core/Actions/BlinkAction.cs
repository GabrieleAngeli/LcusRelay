using LcusRelay.Core.Automation;
using LcusRelay.Core.Config;
using LcusRelay.Core.Relay;

namespace LcusRelay.Core.Actions;

/// <summary>
/// Lampeggia invertendo lo stato corrente del relay e, opzionalmente,
/// ripristina lo stato iniziale al termine.
/// </summary>
public sealed class BlinkAction : IAction
{
    private readonly BlinkActionConfig _cfg;

    public BlinkAction(BlinkActionConfig cfg)
    {
        _cfg = cfg;
    }

    public async Task ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var relay = (IRelayController?)context.Services.GetService(typeof(IRelayController))
                    ?? throw new InvalidOperationException("IRelayController non registrato nei Services.");

        var count = Math.Clamp(_cfg.Count, 1, 100);
        var initial = relay.LastKnownState ?? false;

        try
        {
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Stato invertito rispetto all'iniziale (se iniziale OFF => ON, se iniziale ON => OFF)
                await relay.SetAsync(!initial, cancellationToken).ConfigureAwait(false);
                await Task.Delay(GetOnMs(i), cancellationToken).ConfigureAwait(false);

                // Ritorna a stato iniziale
                await relay.SetAsync(initial, cancellationToken).ConfigureAwait(false);

                // Pausa tra lampeggi (tranne ultimo giro)
                if (i < count - 1)
                {
                    await Task.Delay(GetOffMs(i), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (_cfg.RestoreInitialState)
            {
                await relay.SetAsync(initial, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private int GetOnMs(int index)
        => NormalizeMs(ResolveMs(_cfg.OnMsSequence, _cfg.OnMs, index));

    private int GetOffMs(int index)
        => NormalizeMs(ResolveMs(_cfg.OffMsSequence, _cfg.OffMs, index));

    private static int ResolveMs(IReadOnlyList<int>? seq, int fallback, int index)
    {
        if (seq is { Count: > 0 })
        {
            var i = index % seq.Count;
            return seq[i];
        }

        return fallback;
    }

    private static int NormalizeMs(int value)
        => Math.Clamp(value, 20, 10_000);
}
