using System.Globalization;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Trust;

/// <summary>
/// Weighs a node's supplier answers (F7, <c>docs/TRUST.md</c>). Pure and deterministic: <c>NodeFactory</c> calls it to store
/// the counted figure, and <c>NodeViewMapper</c> calls it again to explain that figure, so the two always agree.
/// <para>
/// Asymmetric trust: a claim that raises the figure is taken as given; a claim that lowers it counts once something backs
/// it (a valid document, the buyer's own records, a person's acceptance). Until then the counted figure keeps the claim's
/// fallback: the lane's default mode, grid power, the invoice's net mass.
/// </para>
/// </summary>
public static class TrustAssessor
{
    /// <summary>Null for a node no supplier has answered yet: there is nothing to weigh.</summary>
    public static TrustResult? Assess(SupplierNode node, Invoice invoice, Lane lane, Category category, FactorTable table)
    {
        if (node.TransportMode is null && node.EnergySource is null)
        {
            return null;
        }
        var inputs = node.Trust ?? TrustInputs.None;
        var invoiceMassT = InvoiceFacts.MassTFromLines(invoice.Extracted.Lines);
        var transitDays = TransitDays(inputs);
        var mode = node.TransportMode ?? lane.ModeDefault;
        var claimed = new ActivityState(mode, node.EnergySource, node.MassT);
        // A mass is the supplier's own when it differs from the invoice's, or when the invoice gives none. Its fallback is
        // the invoice's mass when larger; with no invoice mass, no activity figure at all, so the spend estimate stands.
        var fallback = new ActivityState(
            lane.ModeDefault,
            node.EnergySource is null ? null : EnergySource.Grid,
            node.MassT is { } m && invoiceMassT is { } im && m < im ? im
            : invoiceMassT is null ? null
            : node.MassT);

        decimal? Activity(ActivityState s) => s.MassT is not { } massT
            ? null
            : Estimate.ActivityBasedKg(
                new ActivityInput(massT, lane.DistanceKm.GetValueOrDefault(s.Mode) ?? 0m, s.Mode, category.MaterialKey, s.Energy), table).TotalKg;

        // The figure a state gives: its activity figure, or the spend estimate when it has no mass.
        decimal Figure(ActivityState s) => Activity(s) ?? node.SpendKg;

        var claimedKg = Activity(claimed);
        decimal Impact(ActivityState substituted) => claimedKg is { } c ? Math.Max(0m, Figure(substituted) - c) : 0m;

        var claims = new List<ClaimAssessment>();
        if (node.TransportMode is { } claimedMode)
        {
            var impact = Impact(claimed with { Mode = fallback.Mode });
            claims.Add(ModeClaim(claimedMode, impact, inputs, lane, transitDays));
        }
        if (node.EnergySource is { } energy)
        {
            var impact = Impact(claimed with { Energy = fallback.Energy });
            claims.Add(EnergyClaim(energy, impact, inputs));
        }
        if (node.MassT is { } massT && invoiceMassT != massT)
        {
            var impact = Impact(claimed with { MassT = fallback.MassT });
            claims.Add(MassClaim(massT, invoiceMassT, impact, inputs));
        }

        // The counted figure keeps the fallback of every reduction nothing backs yet, all at once.
        var counted = claimed;
        foreach (var claim in claims.Where(c => c.Reduces && !TrustPolicy.Counts(c.Level)))
        {
            counted = claim.Kind switch
            {
                ClaimKind.Mode => counted with { Mode = fallback.Mode },
                ClaimKind.Energy => counted with { Energy = fallback.Energy },
                _ => counted with { MassT = fallback.MassT },
            };
        }
        var verified = node.Status == NodeStatus.Verified && node.ActivityKg is not null;
        var countedKg = verified ? Figure(counted) : (decimal?)null;
        var kgAtRisk = verified && claimedKg is { } ck && countedKg is { } cn ? Math.Max(0m, cn - ck) : 0m;

        var signals = Signals(node, invoice, inputs);
        var score = Score(claims, signals);
        var contradicted = claims.Any(c => c.Level == EvidenceLevel.Contradicted);
        var band = contradicted || score < TrustPolicy.ModerateFrom ? TrustBand.Review
            : score < TrustPolicy.StrongFrom ? TrustBand.Moderate
            : TrustBand.Strong;
        // A partial answer leaves the node on its spend estimate, so only a contradiction makes it worth a person's look;
        // a verified node waits for one whenever a reduction is not backed or the band says so.
        var needsReview = verified ? band == TrustBand.Review || kgAtRisk > 0m : contradicted;
        var assessment = new TrustAssessment(score, band, kgAtRisk, needsReview, claims, signals);
        return new TrustResult(assessment, verified ? claimedKg : null, countedKg, counted);
    }

    /// <summary>The claim's current value as text, which a person's decision must name (stale pages are refused).</summary>
    public static string? ValueOf(SupplierNode node, ClaimKind kind) => kind switch
    {
        ClaimKind.Mode => node.TransportMode is { } m ? Name(m) : null,
        ClaimKind.Energy => node.EnergySource is { } e ? Name(e) : null,
        ClaimKind.Mass => node.MassT?.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>Days from the supplier's dispatch date to the buyer's receipt date; null unless both are known.</summary>
    public static int? TransitDays(TrustInputs inputs) =>
        inputs.Evidence?.DispatchDate is { } dispatched && inputs.Records?.ReceivedOn is { } received
            ? received.DayNumber - dispatched.DayNumber
            : null;

    private static ClaimAssessment ModeClaim(TransportMode mode, decimal impact, TrustInputs inputs, Lane lane, int? transitDays)
    {
        var reasons = new List<ClaimReason>();
        var review = inputs.ModeReview;
        var evidence = inputs.Evidence;
        var border = inputs.Records?.CustomsBorderMode;
        var lead = lane.LeadDaysByMode.GetValueOrDefault(mode);

        var contradicted = false;
        if (border is { } b && b != mode)
        {
            reasons.Add(new(ClaimReasonCode.CustomsDisagrees, $"The customs declaration gives {Name(b)} at the border, not {Name(mode)}."));
            contradicted = true;
        }
        if (transitDays is { } days && lead is { } l && TooFast(days, l))
        {
            reasons.Add(new(ClaimReasonCode.TransitTooFast, $"Received {days} days after dispatch; {Name(mode)} takes about {l} days on this lane."));
            contradicted = true;
        }
        if (evidence?.ShippingDocType is { } docType && !DocumentIds.Fits(docType, mode))
        {
            reasons.Add(new(ClaimReasonCode.DocumentModeMismatch, $"A {Label(docType)} does not go with {Name(mode)} freight."));
            contradicted = true;
        }

        var corroborated = false;
        if (border == mode)
        {
            reasons.Add(new(ClaimReasonCode.CustomsAgrees, $"The customs declaration gives {Name(mode)} at the border."));
            corroborated = true;
        }
        if (transitDays is { } d and >= 0 && lead is { } leadDays && Fits(d, leadDays)
            && (lane.ModeDefault == mode || lane.LeadDaysByMode.GetValueOrDefault(lane.ModeDefault) is not { } other || !Fits(d, other)))
        {
            reasons.Add(new(ClaimReasonCode.TransitFits, $"Received {d} days after dispatch, as {Name(mode)} takes on this lane."));
            corroborated = true;
        }

        var documented = false;
        if (evidence?.ShippingDocType is { } type && evidence.ShippingDocId is { } id && DocumentIds.Fits(type, mode))
        {
            if (DocumentIds.Problem(type, id) is { } problem)
            {
                reasons.Add(new(ClaimReasonCode.DocumentInvalid, problem));
            }
            else
            {
                reasons.Add(new(ClaimReasonCode.DocumentGiven, $"{Capitalized(Label(type))} {id}."));
                documented = true;
            }
        }

        var level = Decide(review, contradicted, impact, corroborated, documented, reasons, Name(mode));
        return new ClaimAssessment(ClaimKind.Mode, Name(mode), level, impact > 0m, impact, review, reasons);
    }

    private static ClaimAssessment EnergyClaim(EnergySource energy, decimal impact, TrustInputs inputs)
    {
        var reasons = new List<ClaimReason>();
        var review = inputs.EnergyReview;
        var documented = false;
        if (energy == EnergySource.Renewable)
        {
            if (inputs.Evidence is { EnergyCertType: { } type, EnergyCertId: { } id })
            {
                if (DocumentIds.CertificateProblem(id) is { } problem)
                {
                    reasons.Add(new(ClaimReasonCode.CertificateInvalid, problem));
                }
                else
                {
                    reasons.Add(new(ClaimReasonCode.CertificateGiven, $"{Capitalized(Label(type))} {id}."));
                    documented = true;
                }
            }
            else
            {
                reasons.Add(new(ClaimReasonCode.NoCertificate, "Renewable power without a certificate counts as grid power."));
            }
        }
        var level = Decide(review, contradicted: false, impact, corroborated: false, documented, reasons, Name(energy));
        return new ClaimAssessment(ClaimKind.Energy, Name(energy), level, impact > 0m, impact, review, reasons);
    }

    private static ClaimAssessment MassClaim(decimal massT, decimal? invoiceMass, decimal impact, TrustInputs inputs)
    {
        var reasons = new List<ClaimReason>();
        var review = inputs.MassReview;
        var value = massT.ToString(CultureInfo.InvariantCulture);
        var contradicted = false;
        if (invoiceMass is not { } invoiceMassT)
        {
            // Nothing on the invoice to compare it with: Decide marks it unproven, or nothing to back if it raises the figure.
        }
        else if (massT < invoiceMassT)
        {
            reasons.Add(new(ClaimReasonCode.MassBelowInvoice, Invariant($"{massT:0.###} t shipped is less than the invoice's {invoiceMassT:0.###} t of goods.")));
            contradicted = true;
        }
        else if (massT > invoiceMassT * TrustPolicy.MassFarAboveFactor)
        {
            reasons.Add(new(ClaimReasonCode.MassFarAboveInvoice, Invariant($"{massT:0.###} t is more than twice the invoice's {invoiceMassT:0.###} t.")));
        }
        var level = Decide(review, contradicted, impact, corroborated: false, documented: false, reasons, value + " t");
        return new ClaimAssessment(ClaimKind.Mass, value, level, impact > 0m, impact, review, reasons);
    }

    /// <summary>
    /// A person's decision first; then a contradiction; then a claim that lowers nothing needs nothing; then the strongest
    /// backing found.
    /// </summary>
    private static EvidenceLevel Decide(
        ClaimDecision? review, bool contradicted, decimal impact, bool corroborated, bool documented, List<ClaimReason> reasons, string value)
    {
        if (review == ClaimDecision.Rejected)
        {
            reasons.Insert(0, new(ClaimReasonCode.RejectedByBuyer, $"A person rejected {value}."));
            return EvidenceLevel.Contradicted;
        }
        if (review == ClaimDecision.Accepted)
        {
            reasons.Insert(0, new(ClaimReasonCode.AcceptedByBuyer, $"A person accepted {value}."));
            return EvidenceLevel.Reviewed;
        }
        if (contradicted)
        {
            return EvidenceLevel.Contradicted;
        }
        if (impact <= 0m)
        {
            reasons.Insert(0, new(ClaimReasonCode.RaisesFigure, $"{Capitalized(value)} does not lower the figure, so there is nothing to prove."));
            return EvidenceLevel.NotNeeded;
        }
        if (corroborated)
        {
            return EvidenceLevel.Corroborated;
        }
        if (documented)
        {
            return EvidenceLevel.Documented;
        }
        if (!reasons.Any(r => r.Code is ClaimReasonCode.DocumentInvalid or ClaimReasonCode.CertificateInvalid or ClaimReasonCode.NoCertificate))
        {
            reasons.Add(new(ClaimReasonCode.NoEvidence, "Nothing backs this answer yet."));
        }
        return EvidenceLevel.Unproven;
    }

    private static List<TrustSignal> Signals(SupplierNode node, Invoice invoice, TrustInputs inputs)
    {
        var signals = new List<TrustSignal>();
        var answer = inputs.Answer;
        if (answer?.Simulated == true)
        {
            signals.Add(new(TrustSignalCode.SimulatedAnswer, TrustPolicy.SimulatedPenalty, "The demo tool answered, not the supplier."));
        }
        if (answer?.SameNetworkAsLinkMaker == true)
        {
            signals.Add(new(TrustSignalCode.SameNetworkAsLinkMaker, TrustPolicy.SameNetworkPenalty, "Answered from the network the link was made from."));
        }
        if (answer?.LinkAgeSeconds is { } age && age < TrustPolicy.FastAnswerSeconds)
        {
            signals.Add(new(TrustSignalCode.FastAnswer, TrustPolicy.FastAnswerPenalty, $"Answered {age} s after the link was made."));
        }
        if (TaxIds.Check(invoice.VendorCui, node.Country) == TaxIdCheck.Invalid)
        {
            signals.Add(new(TrustSignalCode.SellerTaxIdInvalid, TrustPolicy.TaxIdPenalty, $"The seller's tax id {invoice.VendorCui} fails its check."));
        }
        if (answer?.ModeChangedOnLane == true)
        {
            signals.Add(new(TrustSignalCode.ModeChangedOnLane, TrustPolicy.ModeChangedPenalty, "The supplier's last delivery on this lane went by another mode."));
        }
        if (answer?.PriorRejection == true)
        {
            signals.Add(new(TrustSignalCode.PriorRejection, TrustPolicy.PriorRejectionPenalty, "A person rejected an earlier answer from this supplier."));
        }
        if (TransitDays(inputs) is < 0)
        {
            signals.Add(new(TrustSignalCode.ReceiptBeforeDispatch, 0, "The receipt date is before the dispatch date."));
        }
        return signals;
    }

    private static int Score(IReadOnlyList<ClaimAssessment> claims, IReadOnlyList<TrustSignal> signals)
    {
        var reducing = claims.Where(c => c.Reduces).ToList();
        var total = reducing.Sum(c => c.ImpactKg);
        var evidence = total > 0m
            ? TrustPolicy.EvidencePoints * reducing.Sum(c => c.ImpactKg / total * TrustPolicy.Credit(c.Level))
            : TrustPolicy.EvidencePoints;
        var channel = Math.Max(0, TrustPolicy.ChannelPoints - signals.Sum(s => s.Points));
        var score = (int)Math.Round(evidence + channel, MidpointRounding.AwayFromZero);
        return claims.Any(c => c.Level == EvidenceLevel.Contradicted) ? Math.Min(score, TrustPolicy.ContradictionCap) : score;
    }

    private static bool Fits(int days, int leadDays) =>
        days >= leadDays * TrustPolicy.TransitTooFastShare && days <= leadDays * TrustPolicy.TransitFitsUpTo;

    /// <summary>
    /// The one transit rule, shared with <c>SEA_TOO_FAST</c>: received in under half the mode's lead time. A negative
    /// transit (received before dispatch) is a data error, signalled apart, never a contradiction.
    /// </summary>
    public static bool TooFast(int transitDays, int leadDays) => transitDays >= 0 && transitDays < leadDays * TrustPolicy.TransitTooFastShare;

    /// <summary>An enum value as the wire spells it: "containerNumber", "strong".</summary>
    public static string Camel<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static string Name(TransportMode mode) => mode.ToString().ToLowerInvariant();

    public static string Name(EnergySource energy) => energy.ToString().ToLowerInvariant();

    public static string Label(ShippingDocType type) => type switch
    {
        ShippingDocType.ContainerNumber => "container number",
        ShippingDocType.AirWaybill => "air waybill",
        ShippingDocType.BillOfLading => "bill of lading",
        ShippingDocType.Cmr => "CMR consignment note",
        ShippingDocType.ETransportUit => "e-Transport UIT code",
        ShippingDocType.RailConsignment => "rail consignment note",
        _ => type.ToString(),
    };

    public static string Label(EnergyCertType type) => type switch
    {
        EnergyCertType.GuaranteeOfOrigin => "guarantee of origin",
        EnergyCertType.IRec => "I-REC certificate",
        EnergyCertType.ChinaGec => "China green electricity certificate",
        EnergyCertType.TaiwanTRec => "Taiwan T-REC certificate",
        EnergyCertType.Ppa => "power purchase agreement",
        EnergyCertType.OnSite => "on-site generation record",
        _ => type.ToString(),
    };

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
