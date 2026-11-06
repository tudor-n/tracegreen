using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Copy;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>
/// How the dominant line was classified; <see cref="Confidence"/> is null when nothing could be trusted. <see cref="Rationale"/>
/// is the one-line reason of whatever classified it (the invoice code, the cache or a model).
/// </summary>
public sealed record NodeClassification(string CategoryId, double? Confidence, ClassificationSource Source, string? Rationale = null);

/// <summary>
/// Builds an invoice and its supplier node the same way for the seed and for imports: spend estimate from the total,
/// mass from the unit codes (or the supplier's confirmed mass), cost and lead time from the lane leg, and an activity
/// estimate once the supplier answered and a mass is known. A verification rebuilds the node here so it ends up exactly
/// as the seed would have built it from the same answers.
/// </summary>
public static class NodeFactory
{
    public static Invoice NewInvoice(Guid id, Guid workspaceId, ExtractedInvoice extracted, string xml, DateTimeOffset createdAt) =>
        NewInvoiceFrom(id, workspaceId, extracted, Hashing.RawXmlHash(xml), createdAt, "ubl");

    /// <summary>An invoice from any document: <paramref name="rawHash"/> refuses the same document twice, <paramref name="kind"/> says what it was.</summary>
    public static Invoice NewInvoiceFrom(Guid id, Guid workspaceId, ExtractedInvoice extracted, string rawHash, DateTimeOffset createdAt, string kind) => new(
        id, workspaceId, extracted.Supplier.Name, extracted.Supplier.TaxId, extracted.Customer.TaxId,
        extracted.IssueDate, extracted.Currency, extracted.Total, rawHash, Hashing.InvoiceHash(extracted), extracted, createdAt, kind);

    public static SupplierNode NewNode(
        Guid id,
        Invoice invoice,
        NodeClassification classification,
        Lane lane,
        NodeStatus status,
        TransportMode? transportMode,
        EnergySource? energySource,
        SeedBundle bundle,
        FactorTable table,
        DateTimeOffset createdAt,
        decimal? confirmedMassT = null)
    {
        var extracted = invoice.Extracted;
        var category = bundle.CategoryOf(classification.CategoryId);
        var mode = transportMode ?? lane.ModeDefault;
        var massT = confirmedMassT ?? InvoiceFacts.MassTFromLines(extracted.Lines);
        var spend = Estimate.SpendBasedKg(extracted.Total, extracted.IssueDate, category.SectorKey, table);
        // No activity figure without a mass: the activity estimate would read 0 kg, so the node stays on the spend figure.
        decimal? activity = status == NodeStatus.Estimated || massT is null
            ? null
            : Estimate.ActivityBasedKg(
                new ActivityInput(massT, lane.DistanceKm.GetValueOrDefault(mode) ?? 0m, mode, category.MaterialKey, energySource), table).TotalKg;
        var costPerKg = lane.CostPerKgByMode.GetValueOrDefault(mode) ?? 0m;
        var leadDays = lane.LeadDaysByMode.GetValueOrDefault(mode) ?? 0;

        return new SupplierNode(
            Id: id,
            WorkspaceId: invoice.WorkspaceId,
            InvoiceId: invoice.Id,
            Label: extracted.Supplier.Name,
            Country: extracted.Supplier.Country,
            CategoryId: classification.CategoryId,
            SectorId: category.SectorKey,
            AiConfidence: classification.Confidence,
            ClassificationSource: classification.Source,
            MassT: massT,
            LaneId: lane.Id,
            Status: status,
            SpendKg: spend.Kg,
            ActivityKg: activity,
            SpendEur: extracted.Total,
            CostEur: extracted.Total + (massT ?? 0m) * 1000m * costPerKg,
            LeadDays: leadDays,
            TransportMode: transportMode,
            EnergySource: energySource,
            CreatedAt: createdAt,
            Review: InitialReview(classification.Source),
            ClassificationRationale: Reason(classification.Rationale));
    }

    /// <summary>Longest reason kept, the same cap the AI service applies to a model's rationale.</summary>
    public const int MaxRationaleChars = 200;

    /// <summary>
    /// The reason as a reader may see it: whitespace collapsed, capped, and null when empty or against the copy rules. The
    /// copy rules are checked on the whole text before the cut, so a name the cut would split is still caught, and the cut
    /// never ends inside a surrogate pair (a lone surrogate is not valid UTF-8, and Postgres refuses it).
    /// </summary>
    public static string? Reason(string? rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale))
        {
            return null;
        }
        var text = string.Join(' ', rationale.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (BannedTerms.Any(text))
        {
            return null;
        }
        if (text.Length > MaxRationaleChars)
        {
            var cut = char.IsHighSurrogate(text[MaxRationaleChars - 1]) ? MaxRationaleChars - 1 : MaxRationaleChars;
            text = text[..cut].TrimEnd();
        }
        return BannedTerms.Clean(text);
    }

    /// <summary>
    /// Every AI-mapped category waits for a person, whatever its confidence; an invoice code, the stub and a buyer's own
    /// choice need no review.
    /// </summary>
    public static ClassificationReview InitialReview(ClassificationSource source) =>
        source is ClassificationSource.Cache or ClassificationSource.Model ? ClassificationReview.Pending : ClassificationReview.NotNeeded;
}
