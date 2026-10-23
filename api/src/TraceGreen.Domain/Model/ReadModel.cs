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
