using System.Text.Json.Nodes;

namespace TraceGreen.Domain.Model;

/// <summary>Caller context for buyer-side operations. Today a constant demo workspace; later the authenticated tenant.</summary>
public sealed record Ctx(Guid WorkspaceId, string Actor);

public sealed record Buyer(string Name, string Cui, string Country, string City);

/// <summary>Every number that leaves the carbon math carries its provenance.</summary>
public sealed record FactorLookup(decimal Value, string Unit, string Source, int Year, string RowLabel);

public sealed record Party(string Name, string TaxId, string Country);

public sealed record ExtractedLine(
    string Id,
    string Description,
    decimal Quantity,
    string UnitCode,
    decimal Amount,
    string? OriginCountry,
    string? ClassificationCode,
    string? ClassificationListId);

/// <summary>Canonical invoice shape produced by every parser (UBL today; CII or PDF extraction later).</summary>
public sealed record ExtractedInvoice(
    string Number,
    DateOnly IssueDate,
    string Currency,
    decimal Total,
    Party Supplier,
    Party Customer,
    IReadOnlyList<ExtractedLine> Lines);

public sealed record Invoice(
    Guid Id,
    Guid WorkspaceId,
    string VendorName,
    string VendorCui,
    string BuyerCui,
    DateOnly IssueDate,
    string Currency,
    decimal TotalAmount,
    string RawXmlHash,
    string InvoiceHash,
    ExtractedInvoice Extracted,
    DateTimeOffset CreatedAt);

public sealed record SupplierNode(
    Guid Id,
    Guid WorkspaceId,
    Guid InvoiceId,
    string Label,
    string Country,
    string CategoryId,
    string SectorId,
    double? AiConfidence,
    ClassificationSource ClassificationSource,
    decimal? MassT,
    string LaneId,
    NodeStatus Status,
    decimal SpendKg,
    decimal? ActivityKg,
    decimal SpendEur,
    decimal CostEur,
    int LeadDays,
    TransportMode? TransportMode,
    EnergySource? EnergySource,
    DateTimeOffset CreatedAt)
{
    /// <summary>The number the graph shows: activity-based once verified, otherwise the spend-based estimate.</summary>
    public decimal CarbonKg => Status == NodeStatus.Verified && ActivityKg is { } activity ? activity : SpendKg;
}

/// <summary>The fields a supplier verification may change on a node.</summary>
public sealed record NodePatch(
    NodeStatus Status,
    decimal? ActivityKg,
    TransportMode? TransportMode,
    EnergySource? EnergySource,
    decimal? MassT);

public sealed record Verification(
    Guid Id,
    Guid WorkspaceId,
    Guid NodeId,
    string TokenNonce,
    TransportMode? TransportMode,
    EnergySource? EnergySource,
    decimal? MassTConfirmed,
    DateTimeOffset ConsumedAt,
    string IpHash,
    DateTimeOffset CreatedAt);

public sealed record AuditEntry(
    long Id,
    Guid WorkspaceId,
    DateTimeOffset Ts,
    string Actor,
    string Action,
    JsonNode Payload,
    string PrevHash,
    string Hash);

public sealed record NewAuditEntry(
    Guid WorkspaceId,
    DateTimeOffset Ts,
    string Actor,
    string Action,
    JsonNode Payload,
    string PrevHash,
    string Hash);

/// <summary>Builds the next audit entry from the current chain head; adapters invoke it under their lock or transaction.</summary>
public delegate NewAuditEntry AuditBuilder(AuditEntry? prev);

public sealed record Factor(
    string Id,
    FactorKind Kind,
    string Key,
    decimal Value,
    string Unit,
    string Source,
    int Year,
    string RowLabel);

public sealed record Lane(
    string Id,
    string Origin,
    string Dest,
    TransportMode ModeDefault,
    IReadOnlyDictionary<TransportMode, decimal?> DistanceKm,
    IReadOnlyDictionary<TransportMode, int?> LeadDaysByMode,
    IReadOnlyDictionary<TransportMode, decimal?> CostPerKgByMode,
    string Note);

public sealed record Alternative(
    string Id,
    string NodeCategory,
    string Label,
    string Country,
    string LaneId,
    decimal CostEur,
    decimal MassT);

public sealed record Category(string Id, string Label, string MaterialKey, string SectorKey);

public sealed record Kpis(
    decimal TotalKg,
    double PrimaryShare,
    double EstimatedShare,
    decimal SpendEur,
    int NodeCount,
    int VerifiedCount);

/// <summary>
/// Everything the dashboard renders. <see cref="LastSeq"/> is read before the nodes, so an event published while the
/// snapshot is built has a higher sequence number and is applied on top rather than lost.
/// </summary>
public sealed record GraphSnapshot(
    Buyer Buyer,
    IReadOnlyList<NodeView> Nodes,
    Kpis Kpis,
    IReadOnlyList<AuditEntry> Audit,
    long LastSeq,
    string Mode);
