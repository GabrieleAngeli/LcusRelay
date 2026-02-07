using System;

namespace LcusRelay.Core.Automation;

public interface IRelayStateStore
{
    RelayStateSnapshot Snapshot { get; }
    void RecordRelayChange(bool state, string trigger, string? series);
}

public sealed record RelayStateSnapshot(
    bool? LastState,
    string? LastTrigger,
    string? LastSeries,
    DateTimeOffset? LastUpdatedUtc
);
