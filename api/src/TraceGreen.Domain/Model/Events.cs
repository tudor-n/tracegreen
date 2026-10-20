namespace TraceGreen.Domain.Model;

/// <summary>
/// Notifications pushed to dashboards. Each carries the fresh node and KPIs so clients never recompute carbon;
/// <see cref="Seq"/> is monotonic per workspace and lets a reconnecting client ask for a replay.
/// </summary>
public abstract record DomainEvent(long Seq, DateTimeOffset Ts, Guid WorkspaceId)
{
    public abstract string Type { get; }
}

public sealed record NodeCreated(long Seq, DateTimeOffset Ts, Guid WorkspaceId, SupplierNode Node, Kpis Kpis)
    : DomainEvent(Seq, Ts, WorkspaceId)
{
    public override string Type => "node.created";
}

public sealed record NodeUpdated(long Seq, DateTimeOffset Ts, Guid WorkspaceId, SupplierNode Node, Kpis Kpis)
    : DomainEvent(Seq, Ts, WorkspaceId)
{
    public override string Type => "node.updated";
}

public sealed record NodeVerified(long Seq, DateTimeOffset Ts, Guid WorkspaceId, SupplierNode Node, Verification Verification, Kpis Kpis)
    : DomainEvent(Seq, Ts, WorkspaceId)
{
    public override string Type => "node.verified";
}

public sealed record AuditAppended(long Seq, DateTimeOffset Ts, Guid WorkspaceId, AuditEntry Entry)
    : DomainEvent(Seq, Ts, WorkspaceId)
{
    public override string Type => "audit.appended";
}

public sealed record ScenarioSaved(long Seq, DateTimeOffset Ts, Guid WorkspaceId, Scenario Scenario)
    : DomainEvent(Seq, Ts, WorkspaceId)
{
    public override string Type => "scenario.saved";
}

public sealed record GraphReset(long Seq, DateTimeOffset Ts, Guid WorkspaceId)
    : DomainEvent(Seq, Ts, WorkspaceId)
{
    public override string Type => "graph.reset";
}
