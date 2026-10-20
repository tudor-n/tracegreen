using System.Text.Json.Serialization;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Carbon;

public enum MacKind
{
    [JsonStringEnumMemberName("abatement")] Abatement,
    [JsonStringEnumMemberName("not-abatement")] NotAbatement,
}

/// <summary>Serialised with the labels the UI renders.</summary>
public enum MacBadge
{
    [JsonStringEnumMemberName("win-win")] WinWin,
    [JsonStringEnumMemberName("below-ets")] BelowEts,
    [JsonStringEnumMemberName("premium")] Premium,
    [JsonStringEnumMemberName("not-abatement")] NotAbatement,
}

/// <summary>Marginal abatement cost. <see cref="MacEurPerT"/> is null when the change saves no carbon.</summary>
public sealed record MacResult(MacKind Kind, decimal? MacEurPerT, decimal DeltaCostEur, decimal DeltaCarbonT);

public sealed record ScenarioDelta(decimal CarbonKg, decimal CostEur, int LeadDays);

public sealed record ScenarioResult(
    decimal CarbonKg,
    decimal CostEur,
    int LeadDays,
    ScenarioDelta Delta,
    MacResult Mac,
    MacBadge Badge,
    IReadOnlyList<FactorLookup> Inputs,
    bool MassUnknown,
    bool Illustrative);

public sealed class ModeNotAvailableException(string laneId, TransportMode mode)
    : Exception($"Lane {laneId} has no {mode} leg")
{
    public string LaneId { get; } = laneId;
    public TransportMode Mode { get; } = mode;
}
