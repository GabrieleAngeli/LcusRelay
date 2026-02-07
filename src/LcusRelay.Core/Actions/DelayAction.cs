
using LcusRelay.Core.Automation;
using LcusRelay.Core.Config;

namespace LcusRelay.Core.Actions;

public sealed class DelayAction : IAction
{
    private readonly DelayActionConfig _cfg;

    public DelayAction(DelayActionConfig cfg)
    {
        _cfg = cfg;
    }

    public Task ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
        => Task.Delay(Math.Max(0, _cfg.Milliseconds), cancellationToken);
}
