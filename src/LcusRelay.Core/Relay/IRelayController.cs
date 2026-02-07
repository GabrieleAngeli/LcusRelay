
namespace LcusRelay.Core.Relay;

public interface IRelayController
{
    /// <summary>Imposta ON/OFF.</summary>
    Task SetAsync(bool on, CancellationToken cancellationToken = default);

    /// <summary>Ultimo stato conosciuto (best effort).</summary>
    bool? LastKnownState { get; }
}
