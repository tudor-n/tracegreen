using TraceGreen.Domain.Carbon;

namespace TraceGreen.Domain.Model;

public enum ScenarioKind { Mode, Supplier }

public sealed record ScenarioChange(TransportMode? Mode, string? AlternativeId);

/// <summary>A saved what-if. Never overwrites the node it was computed from.</summary>
public sealed record Scenario(
    Guid Id,
    Guid WorkspaceId,
    Guid NodeId,
    ScenarioKind Kind,
    ScenarioChange Change,
    ScenarioResult Result,
    DateTimeOffset CreatedAt);
