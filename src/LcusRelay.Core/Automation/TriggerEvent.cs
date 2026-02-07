
namespace LcusRelay.Core.Automation;

public sealed class TriggerEvent
{
    public TriggerEvent(string name, IReadOnlyDictionary<string, string>? data = null)
    {
        Name = name;
        Data = data ?? new Dictionary<string, string>();
    }

    public string Name { get; }
    public IReadOnlyDictionary<string, string> Data { get; }
}
