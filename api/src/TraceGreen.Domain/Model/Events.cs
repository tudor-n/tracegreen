using System.Reflection;
using System.Text.Json.Serialization;

namespace TraceGreen.Domain.Model;

/// <summary>
/// Notifications pushed to dashboards. Each carries the fresh node and KPIs so clients never recompute carbon;
/// <see cref="Seq"/> is monotonic per workspace and lets a reconnecting client ask for a replay.
/// On the wire the discriminator <c>type</c> is written first; <see cref="Type"/> reads the same attribute and is also the SSE event name.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NodeCreated), "node.created")]
[JsonDerivedType(typeof(NodeUpdated), "node.updated")]
[JsonDerivedType(typeof(NodeVerified), "node.verified")]
[JsonDerivedType(typeof(AuditAppended), "audit.appended")]
[JsonDerivedType(typeof(ScenarioSaved), "scenario.saved")]
[JsonDerivedType(typeof(GraphReset), "graph.reset")]
public abstract record DomainEvent(long Seq, DateTimeOffset Ts, Guid WorkspaceId)
{
    private static readonly Dictionary<Type, string> Names = typeof(DomainEvent)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    [JsonIgnore]
    public string Type => Names[GetType()];
}

public sealed record NodeCreated(long Seq, DateTimeOffset Ts, Guid WorkspaceId, NodeView Node, Kpis Kpis)
    : DomainEvent(Seq, Ts, WorkspaceId);

public sealed record NodeUpdated(long Seq, DateTimeOffset Ts, Guid WorkspaceId, NodeView Node, Kpis Kpis)
    : DomainEvent(Seq, Ts, WorkspaceId);

public sealed record NodeVerified(long Seq, DateTimeOffset Ts, Guid WorkspaceId, NodeView Node, VerificationView Verification, Kpis Kpis)
    : DomainEvent(Seq, Ts, WorkspaceId);

public sealed record AuditAppended(long Seq, DateTimeOffset Ts, Guid WorkspaceId, AuditEntry Entry)
    : DomainEvent(Seq, Ts, WorkspaceId);

public sealed record ScenarioSaved(long Seq, DateTimeOffset Ts, Guid WorkspaceId, Scenario Scenario)
    : DomainEvent(Seq, Ts, WorkspaceId);

public sealed record GraphReset(long Seq, DateTimeOffset Ts, Guid WorkspaceId)
    : DomainEvent(Seq, Ts, WorkspaceId);
