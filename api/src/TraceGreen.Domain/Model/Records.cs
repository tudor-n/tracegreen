using System.Text.Json.Nodes;
using TraceGreen.Domain.Trust;

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
    DateTimeOffset CreatedAt,
    string DocumentKind = "ubl");

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
    DateTimeOffset CreatedAt,
    ClassificationReview Review = ClassificationReview.NotNeeded,
    DateTimeOffset? ReviewedAt = null,
    string? ClassificationRationale = null,
    TrustInputs? Trust = null,
    decimal? CountedKg = null,
    TargetChoice? Target = null)
{
    /// <summary>
    /// The number the graph shows and the KPIs add up: once verified, the activity figure as counted (each reduction nothing
    /// backs yet replaced by its fallback, F7), otherwise the spend-based estimate. <see cref="ActivityKg"/> is the activity
    /// figure as the supplier claimed it; <see cref="CountedKg"/> is null on rows written before the trust model, which
    /// then count as claimed.
    /// </summary>
    public decimal CarbonKg => Status == NodeStatus.Verified && ActivityKg is { } activity ? CountedKg ?? activity : SpendKg;
}

/// <summary>
/// The fields a classification review changes on a node: the category and what follows from it (sector, spend figure,
/// activity figure), the source <see cref="ClassificationSource.Buyer"/> with no AI confidence, and the review state.
/// Cost and lead time depend on the lane and the mass only, so they stay.
/// </summary>
public sealed record ClassificationPatch(
    string CategoryId,
    string SectorId,
    ClassificationSource Source,
    double? Confidence,
    ClassificationReview Review,
    DateTimeOffset ReviewedAt,
    decimal SpendKg,
    decimal? ActivityKg,
    decimal? CountedKg = null);

/// <summary>
/// What a review writes: the node's new classification fields, the audit entry that records the change, and the
/// buyer's category remembered under <see cref="LineKey"/> (the <c>CacheKey</c> of the dominant line's description), so a
/// later line with the same description gets it before the AI is asked.
/// </summary>
public sealed record ClassificationWrite(ClassificationPatch Patch, string LineKey, AuditBuilder Audit);

/// <summary>
/// Computes a review's write from the node as the repository holds it under the write's lock, so the recomputed figures
/// always rest on the node's current answers and mass, whatever landed just before.
/// </summary>
public delegate ClassificationWrite ClassificationReviewer(SupplierNode current);

/// <summary>
/// The fields a supplier verification may change on a node; cost and lead time follow the confirmed mode. <see cref="Trust"/>
/// is what the trust assessment reads after this answer (its evidence and how it arrived), <see cref="CountedKg"/> the
/// figure it counts.
/// </summary>
public sealed record NodePatch(
    NodeStatus Status,
    decimal? ActivityKg,
    TransportMode? TransportMode,
    EnergySource? EnergySource,
    decimal? MassT,
    decimal CostEur,
    int LeadDays,
    TrustInputs? Trust = null,
    decimal? CountedKg = null);

/// <summary>
/// A change to what a node's trust assessment reads (the buyer's records, a person's decision on a claim) or to its target,
/// with the counted figure recomputed from it and the audit entry that records it. Written whole: the computing side copies
/// what it does not change from the node it was given.
/// </summary>
public sealed record TrustWrite(TrustInputs? Trust, decimal? CountedKg, TargetChoice? Target, AuditBuilder Audit);

/// <summary>
/// Computes a trust write from the node as the repository holds it under the write's lock; null refuses the write (the node
/// no longer matches what the caller's page showed), and nothing is written.
/// </summary>
public delegate TrustWrite? TrustUpdater(SupplierNode current);

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
    DateTimeOffset CreatedAt,
    string? TermsVersion,
    string? TermsSha256,
    AnswerEvidence? Evidence = null);

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

/// <summary>
/// <see cref="KgAtRisk"/>: the reductions supplier answers claim that nothing backs yet, summed over the verified nodes. The
/// totals already leave them out (each node counts its fallback), so this is what proving them could take off.
/// </summary>
public sealed record Kpis(
    decimal TotalKg,
    double PrimaryShare,
    double EstimatedShare,
    decimal SpendEur,
    int NodeCount,
    int VerifiedCount,
    decimal KgAtRisk);

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
