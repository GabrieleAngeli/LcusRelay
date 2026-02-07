
using System.Diagnostics;
using LcusRelay.Core.Automation;
using LcusRelay.Core.Config;

namespace LcusRelay.Core.Actions;

public sealed class RunProcessAction : IAction
{
    private readonly RunProcessActionConfig _cfg;

    public RunProcessAction(RunProcessActionConfig cfg)
    {
        _cfg = cfg;
    }

    public Task ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _cfg.FileName,
            Arguments = _cfg.Arguments ?? "",
            UseShellExecute = true,
            WindowStyle = _cfg.HiddenWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };

        Process.Start(psi);
        return Task.CompletedTask;
    }
}
