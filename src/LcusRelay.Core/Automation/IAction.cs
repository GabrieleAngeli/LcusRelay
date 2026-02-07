
namespace LcusRelay.Core.Automation;

public interface IAction
{
    Task ExecuteAsync(ActionContext context, CancellationToken cancellationToken);
}

public sealed class ActionContext
{
    public required string Trigger { get; init; }
    public required IServiceProvider Services { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}
