using System.Text.Json.Serialization;

namespace TraceGreen.Domain.Model;

/// <summary>Which estimate <see cref="NodeView.CarbonKg"/> comes from. Serialised "spend" | "activity".</summary>
public enum CarbonBasis { Spend, Activity }

/// <summary>Qualitative uncertainty (ruling 4), shown as a label and never as ±%. Serialised "low" | "medium" | "high".</summary>
public enum Uncertainty { Low, Medium, High }

/// <summary>A carbon number with the factor rows it was computed from, in formula order (the "i" icon).</summary>
public sealed record CarbonFigure(decimal Kg, IReadOnlyList<FactorLookup> Factors);

/// <summary>
/// How the node's category was decided. <see cref="AiMapped"/> is true when the category came from the AI (live or
/// pre-computed cache), so the UI must label it "AI-mapped". <see cref="LineDescription"/> is invoice text: render it as text.
/// </summary>
public sealed record ClassificationView(
    string CategoryId,
    string CategoryLabel,
    ClassificationSource Source,
    double? Confidence,
    bool AiMapped,
    string LineDescription);

/// <summary>
/// The node's freight lane. <see cref="Mode"/> is the confirmed mode, or the lane default before the supplier answers;
/// <see cref="Modes"/> lists the modes the lane has data for. Distances, costs and lead times are illustrative.
/// </summary>
public sealed record LaneView(
    string Id,
    string Origin,
    string Dest,
    TransportMode Mode,
    TransportMode ModeDefault,
    IReadOnlyList<TransportMode> Modes,
    decimal? DistanceKm,
    bool Illustrative);

public sealed record InvoiceSummary(Guid Id, string Number, DateOnly IssueDate, string Currency, decimal Total);

/// <summary>
/// The node as every client sees it, in the snapshot and in events alike, so the SPA renders without computing.
/// Provenance line (ruling 3): <see cref="Basis"/> says which figure <see cref="CarbonKg"/> is, and that figure's
/// <see cref="CarbonFigure.Factors"/> carry source, year and row label. A PLACEHOLDER source is shown as is.
/// <see cref="CostEur"/> and <see cref="LeadDays"/> derive from the lane and are illustrative like it.
/// <see cref="Warnings"/> are derived from the supplier's answers on every read (D2: shown, never blocking).
/// </summary>
public sealed record NodeView(
    Guid Id,
    string Label,
    string Country,
    NodeStatus Status,
    decimal CarbonKg,
    CarbonBasis Basis,
    Uncertainty Uncertainty,
    CarbonFigure Spend,
    CarbonFigure? Activity,
    ClassificationView Classification,
    InvoiceSummary Invoice,
    LaneView Lane,
    decimal? MassT,
    TransportMode? TransportMode,
    EnergySource? EnergySource,
    decimal SpendEur,
    decimal CostEur,
    int LeadDays,
    IReadOnlyList<PlausibilityWarning> Warnings,
    DateTimeOffset CreatedAt);

/// <summary>What a dashboard learns about a supplier answer: never the nonce or the address hash.</summary>
public sealed record VerificationView(
    Guid Id,
    TransportMode? TransportMode,
    EnergySource? EnergySource,
    decimal? MassTConfirmed,
    DateTimeOffset ConsumedAt);

/// <summary>Answer to an import: the created node and the KPIs after it, the same values the <c>node.created</c> event carries.</summary>
public sealed record IngestResult(NodeView Node, Kpis Kpis);

/// <summary>
/// Plausibility rules over a supplier's answers, serialised by their rule names. <c>SEA_TOO_FAST</c> needs dispatch and
/// delivery dates that no invoice carries yet (D3), so it is part of the contract but not evaluated.
/// </summary>
public enum PlausibilityCode
{
    [JsonStringEnumMemberName("AIR_HEAVY")] AirHeavy,
    [JsonStringEnumMemberName("SEA_TOO_FAST")] SeaTooFast,
}

/// <summary>A "please reconfirm" note; it never blocks a verification. <see cref="Detail"/> is English for logs and tooltips; UIs key on <see cref="Code"/>.</summary>
public sealed record PlausibilityWarning(PlausibilityCode Code, string Detail);

/// <summary>A signed single-use verification link. The token exists only inside <see cref="Url"/>.</summary>
public sealed record LinkGrant(Guid NodeId, string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// What the supplier's phone page needs to ask its two questions, and nothing personal: company names, the invoiced item,
/// the lane's modes with the default pre-selected, and the energy answers with grid pre-selected.
/// <see cref="ItemDescription"/> is invoice text: render it as text.
/// </summary>
public sealed record VerifyContext(
    Guid NodeId,
    string SupplierLabel,
    string BuyerName,
    string InvoiceNumber,
    DateOnly IssueDate,
    string ItemDescription,
    string Origin,
    string Dest,
    IReadOnlyList<TransportMode> Modes,
    TransportMode DefaultMode,
    IReadOnlyList<EnergySource> EnergySources,
    EnergySource DefaultEnergy,
    decimal? MassT,
    DateTimeOffset ExpiresAt);

/// <summary>The supplier's answers. A null mode or energy source is a skipped question, which makes the node PARTIAL.</summary>
public sealed record VerifyAnswer(string Token, TransportMode? TransportMode, EnergySource? EnergySource, decimal? MassT);

/// <summary>What the phone page shows after a submit: the node's new status and any plausibility warnings.</summary>
public sealed record VerifyOutcome(Guid NodeId, NodeStatus Status, IReadOnlyList<PlausibilityWarning> Warnings);
