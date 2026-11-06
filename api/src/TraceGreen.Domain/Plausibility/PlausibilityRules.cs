using System.Globalization;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Trust;

namespace TraceGreen.Domain.Plausibility;

/// <summary>
/// Checks a supplier's answers for combinations worth a second look. Warnings never block a verification (D2): the phone
/// shows a "please reconfirm" banner and the dashboard marks the node. Only confirmed answers are checked, so a node still
/// on its lane's default mode carries no warning. <c>SEA_TOO_FAST</c> needs the supplier's dispatch date and the buyer's
/// goods-receipt date (F7); <c>MASS_BELOW_INVOICE</c> needs a confirmed mass and an invoice mass.
/// </summary>
public static class PlausibilityRules
{
    /// <summary><c>AIR_HEAVY</c> fires above this mass by air. The demo invoice N1 is 2.3 t, so it fires there on purpose.</summary>
    public const decimal AirHeavyThresholdT = 2m;

    public static IReadOnlyList<PlausibilityWarning> Check(TransportMode? confirmedMode, decimal? massT) =>
        Check(confirmedMode, massT, invoiceMassT: null, transitDays: null, seaLeadDays: null);

    /// <param name="invoiceMassT">The invoice's net goods mass; a shipping mass below it is physically impossible.</param>
    /// <param name="transitDays">Days from the supplier's dispatch date to the buyer's receipt date, when both are known.</param>
    /// <param name="seaLeadDays">The lane's sea lead time; a sea delivery received in under half of it did not go by sea.</param>
    public static IReadOnlyList<PlausibilityWarning> Check(
        TransportMode? confirmedMode, decimal? massT, decimal? invoiceMassT, int? transitDays, int? seaLeadDays)
    {
        var warnings = new List<PlausibilityWarning>();
        if (confirmedMode == TransportMode.Air && massT > AirHeavyThresholdT)
        {
            warnings.Add(new PlausibilityWarning(
                PlausibilityCode.AirHeavy,
                string.Create(CultureInfo.InvariantCulture, $"Air freight confirmed for {massT:0.###} t; more than {AirHeavyThresholdT} t by air is unusual.")));
        }
        if (confirmedMode == TransportMode.Sea && transitDays is { } days && seaLeadDays is { } lead && TrustAssessor.TooFast(days, lead))
        {
            warnings.Add(new PlausibilityWarning(
                PlausibilityCode.SeaTooFast,
                string.Create(CultureInfo.InvariantCulture, $"Sea freight confirmed, but received {days} days after dispatch; sea takes about {lead} days on this lane.")));
        }
        if (massT is { } shipped && invoiceMassT is { } goods && shipped < goods)
        {
            warnings.Add(new PlausibilityWarning(
                PlausibilityCode.MassBelowInvoice,
                string.Create(CultureInfo.InvariantCulture, $"{shipped:0.###} t confirmed is less than the invoice's {goods:0.###} t of goods.")));
        }
        return warnings;
    }

    /// <summary>The warnings for a node as stored: its answers, the invoice's mass and the dates its trust inputs hold.</summary>
    public static IReadOnlyList<PlausibilityWarning> For(SupplierNode node, decimal? invoiceMassT, Lane lane) =>
        Check(
            node.TransportMode, node.MassT, invoiceMassT,
            node.Trust is { } trust ? TrustAssessor.TransitDays(trust) : null,
            lane.LeadDaysByMode.GetValueOrDefault(TransportMode.Sea));
}
