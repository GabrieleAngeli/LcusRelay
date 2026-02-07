
using LcusRelay.Core.Actions;
using LcusRelay.Core.Config;

namespace LcusRelay.Core.Automation;

public static class ActionFactory
{
    public static IAction Create(ActionConfig cfg, IServiceProvider services)
        => cfg switch
        {
            RelayActionConfig r => new RelayAction(r),
            BlinkActionConfig b => new BlinkAction(b),
            RunProcessActionConfig p => new RunProcessAction(p),
            WebhookActionConfig w => new WebhookAction(w),
            DelayActionConfig d => new DelayAction(d),
            _ => throw new NotSupportedException($"Action type non supportato: {cfg.GetType().Name}")
        };
}
