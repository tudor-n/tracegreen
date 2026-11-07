using System.Text.Json.Serialization;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Trust;

/// <summary>A supplier answer whose truth the assessment weighs. Serialised "mode" | "energy" | "mass".</summary>
public enum ClaimKind { Mode, Energy, Mass }

/// <summary>
/// How well a claim is backed, weakest first. <see cref="NotNeeded"/>: the claim does not lower the figure, so there is
/// nothing to prove (a claim that raises the figure is taken as given). Serialised camelCase.
/// </summary>
public enum EvidenceLevel { Contradicted, Unproven, Documented, Corroborated, Reviewed, NotNeeded }

/// <summary>
/// The shipping document a supplier can name for a delivery. Serialised camelCase: "containerNumber" (ISO 6346),
/// "airWaybill", "billOfLading", "cmr" (road consignment note), "eTransportUit" (Romania's e-Transport code),
/// "railConsignment" (CIM/SMGS).
/// </summary>
public enum ShippingDocType { ContainerNumber, AirWaybill, BillOfLading, Cmr, ETransportUit, RailConsignment }

/// <summary>
/// The instrument behind a renewable-power answer. Serialised camelCase: "guaranteeOfOrigin" (EU), "iRec", "chinaGec",
/// "taiwanTRec", "ppa" (a power purchase agreement), "onSite" (own generation).
/// </summary>
public enum EnergyCertType { GuaranteeOfOrigin, IRec, ChinaGec, TaiwanTRec, Ppa, OnSite }

/// <summary>A person's decision on one claim. Serialised "accepted" | "rejected".</summary>
public enum ClaimDecision { Accepted, Rejected }

/// <summary>Serialised "strong" | "moderate" | "review".</summary>
public enum TrustBand { Strong, Moderate, Review }

/// <summary>
/// What the supplier may give besides the answers, all optional: the shipping document for the chosen mode, the dispatch
/// date, and the certificate behind a renewable answer. No personal data.
/// </summary>
public sealed record AnswerEvidence(
    ShippingDocType? ShippingDocType = null,
    string? ShippingDocId = null,
    DateOnly? DispatchDate = null,
    EnergyCertType? EnergyCertType = null,
    string? EnergyCertId = null)
{
    [JsonIgnore]
    public bool IsEmpty => ShippingDocType is null && ShippingDocId is null && DispatchDate is null && EnergyCertType is null && EnergyCertId is null;

    /// <summary>
    /// The evidence a delivery rests on after a new answer: what the answer gives replaces what was there; what it leaves
    /// out is kept only while the answer it backs stays the same (a document for sea says nothing about rail).
    /// </summary>
    public static AnswerEvidence? Merge(
        AnswerEvidence? earlier, AnswerEvidence? given, bool modeChanged, bool energyChanged)
    {
        var keepShipping = earlier is not null && !modeChanged;
        var keepEnergy = earlier is not null && !energyChanged;
        var shippingGiven = given?.ShippingDocType is not null;
        var energyGiven = given?.EnergyCertType is not null;
        var merged = new AnswerEvidence(
            ShippingDocType: shippingGiven ? given!.ShippingDocType : keepShipping ? earlier!.ShippingDocType : null,
            ShippingDocId: shippingGiven ? given!.ShippingDocId : keepShipping ? earlier!.ShippingDocId : null,
            DispatchDate: given?.DispatchDate ?? (keepShipping ? earlier!.DispatchDate : null),
            EnergyCertType: energyGiven ? given!.EnergyCertType : keepEnergy ? earlier!.EnergyCertType : null,
            EnergyCertId: energyGiven ? given!.EnergyCertId : keepEnergy ? earlier!.EnergyCertId : null);
        return merged.IsEmpty ? null : merged;
    }
}

/// <summary>
/// What the buyer's own records say about the delivery: the goods-receipt date, and for an import from outside the EU the
/// mode of transport at the border and the MRN from the customs declaration (the buyer is the importer).
/// </summary>
public sealed record BuyerRecords(DateOnly? ReceivedOn = null, TransportMode? CustomsBorderMode = null, string? CustomsMrn = null)
{
    [JsonIgnore]
    public bool IsEmpty => ReceivedOn is null && CustomsBorderMode is null && CustomsMrn is null;
}

/// <summary>
/// What was known about an answer when it arrived. <see cref="SameNetworkAsLinkMaker"/> compares keyed hashes of the
/// addresses (null when either is unknown); <see cref="LinkAgeSeconds"/> is the time from the link being made to the
/// answer; the two history flags look at the same supplier's other deliveries.
/// </summary>
public sealed record AnswerContext(
    bool? SameNetworkAsLinkMaker, int? LinkAgeSeconds, bool ModeChangedOnLane, bool PriorRejection, bool Simulated);

/// <summary>
/// Everything a trust assessment reads that is not already on the node or its invoice, stored with the node as one
/// document. A person's decision per claim sits in <see cref="ModeReview"/>, <see cref="EnergyReview"/> and
/// <see cref="MassReview"/>.
/// </summary>
public sealed record TrustInputs(
    AnswerEvidence? Evidence = null,
    AnswerContext? Answer = null,
    BuyerRecords? Records = null,
    ClaimDecision? ModeReview = null,
    ClaimDecision? EnergyReview = null,
    ClaimDecision? MassReview = null)
{
    public static readonly TrustInputs None = new();

    public ClaimDecision? ReviewOf(ClaimKind kind) => kind switch
    {
        ClaimKind.Mode => ModeReview,
        ClaimKind.Energy => EnergyReview,
        ClaimKind.Mass => MassReview,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public TrustInputs WithReview(ClaimKind kind, ClaimDecision? decision) => kind switch
    {
        ClaimKind.Mode => this with { ModeReview = decision },
        ClaimKind.Energy => this with { EnergyReview = decision },
        ClaimKind.Mass => this with { MassReview = decision },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [JsonIgnore]
    public bool HasRejection => ModeReview == ClaimDecision.Rejected || EnergyReview == ClaimDecision.Rejected || MassReview == ClaimDecision.Rejected;
}

/// <summary>
/// The buyer's choice for a node's target (the best state the delivery could reach): a mode, a power source, or an
/// alternative supplier from the node's <c>alternatives</c>. A null field takes the default (the lowest-emission mode on
/// the lane, renewable power).
/// </summary>
public sealed record TargetChoice(TransportMode? Mode = null, EnergySource? Energy = null, string? AlternativeId = null)
{
    [JsonIgnore]
    public bool IsEmpty => Mode is null && Energy is null && AlternativeId is null;
}

/// <summary>Why a claim has its level, by code; UIs key on <see cref="ClaimReason.Code"/>. Serialised by the names below.</summary>
public enum ClaimReasonCode
{
    [JsonStringEnumMemberName("NO_EVIDENCE")] NoEvidence,
    [JsonStringEnumMemberName("DOCUMENT_GIVEN")] DocumentGiven,
    [JsonStringEnumMemberName("DOCUMENT_INVALID")] DocumentInvalid,
    [JsonStringEnumMemberName("DOCUMENT_MODE_MISMATCH")] DocumentModeMismatch,
    [JsonStringEnumMemberName("CUSTOMS_AGREES")] CustomsAgrees,
    [JsonStringEnumMemberName("CUSTOMS_DISAGREES")] CustomsDisagrees,
    [JsonStringEnumMemberName("TRANSIT_FITS")] TransitFits,
    [JsonStringEnumMemberName("TRANSIT_TOO_FAST")] TransitTooFast,
    [JsonStringEnumMemberName("CERTIFICATE_GIVEN")] CertificateGiven,
    [JsonStringEnumMemberName("CERTIFICATE_INVALID")] CertificateInvalid,
    [JsonStringEnumMemberName("NO_CERTIFICATE")] NoCertificate,
    [JsonStringEnumMemberName("MASS_BELOW_INVOICE")] MassBelowInvoice,
    [JsonStringEnumMemberName("MASS_FAR_ABOVE_INVOICE")] MassFarAboveInvoice,
    [JsonStringEnumMemberName("RAISES_FIGURE")] RaisesFigure,
    [JsonStringEnumMemberName("ACCEPTED_BY_BUYER")] AcceptedByBuyer,
    [JsonStringEnumMemberName("REJECTED_BY_BUYER")] RejectedByBuyer,
}

/// <summary><see cref="Detail"/> is English for logs and tooltips; UIs key on <see cref="Code"/>.</summary>
public sealed record ClaimReason(ClaimReasonCode Code, string Detail);

/// <summary>Something about how an answer arrived, or about the supplier, that lowers trust in it. Serialised by the names below.</summary>
public enum TrustSignalCode
{
    [JsonStringEnumMemberName("SAME_NETWORK_AS_LINK_MAKER")] SameNetworkAsLinkMaker,
    [JsonStringEnumMemberName("FAST_ANSWER")] FastAnswer,
    [JsonStringEnumMemberName("SELLER_TAX_ID_INVALID")] SellerTaxIdInvalid,
    [JsonStringEnumMemberName("MODE_CHANGED_ON_LANE")] ModeChangedOnLane,
    [JsonStringEnumMemberName("PRIOR_REJECTION")] PriorRejection,
    [JsonStringEnumMemberName("SIMULATED_ANSWER")] SimulatedAnswer,
    [JsonStringEnumMemberName("RECEIPT_BEFORE_DISPATCH")] ReceiptBeforeDispatch,
}

/// <summary><see cref="Points"/> is what the signal takes off the score's channel part; <see cref="Detail"/> is English.</summary>
public sealed record TrustSignal(TrustSignalCode Code, int Points, string Detail);

/// <summary>
/// One answer as the assessment sees it. <see cref="Value"/> is the claim as text ("sea", "renewable", or the mass in
/// tonnes, invariant culture), which a person's decision must name, so a decision made on a stale page is refused.
/// <see cref="Reduces"/>: the claim lowers the figure against its fallback, by <see cref="ImpactKg"/>.
/// </summary>
public sealed record ClaimAssessment(
    ClaimKind Kind,
    string Value,
    EvidenceLevel Level,
    bool Reduces,
    decimal ImpactKg,
    ClaimDecision? Review,
    IReadOnlyList<ClaimReason> Reasons);

/// <summary>
/// How far a node's supplier answers can be believed (F7). <see cref="Score"/> is 0–100: 70 points for the evidence behind
/// the claims that lower the figure (weighted by how much each lowers it), 30 for how the answer arrived. Any contradiction
/// caps it and sends the node to review. <see cref="KgAtRisk"/> is how much of the figure rests on reductions nothing backs
/// yet: the counted figure adds them back. <see cref="NeedsReview"/> puts the node on the dashboard's list to check.
/// </summary>
public sealed record TrustAssessment(
    int Score,
    TrustBand Band,
    decimal KgAtRisk,
    bool NeedsReview,
    IReadOnlyList<ClaimAssessment> Claims,
    IReadOnlyList<TrustSignal> Signals);

/// <summary>
/// The answers an activity figure is computed from: the mode, the power source and the mass. A null mass means no
/// activity figure: the spend estimate stands.
/// </summary>
public sealed record ActivityState(TransportMode Mode, EnergySource? Energy, decimal? MassT);

/// <summary>
/// The assessment and the two activity figures behind it: as the supplier claimed, and as counted (null unless the node
/// is VERIFIED). <see cref="CountedState"/> is what the counted figure rests on: each answer nothing backs yet replaced by
/// its fallback. The simulator and the factor rows on screen use it, so they describe the figure that counts.
/// </summary>
public sealed record TrustResult(TrustAssessment Assessment, decimal? ClaimedKg, decimal? CountedKg, ActivityState CountedState);
