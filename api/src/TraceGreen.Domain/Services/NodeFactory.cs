using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>How the dominant line was classified; <see cref="Confidence"/> is null when nothing could be trusted.</summary>
public sealed record NodeClassification(string CategoryId, double? Confidence, ClassificationSource Source);

/// <summary>
/// Builds an invoice and its supplier node the same way for the seed and for imports: spend estimate from the total,
/// mass from the unit codes, cost and lead time from the lane leg, and an activity estimate once a mode is known.
/// </summary>
public static class NodeFactory
{
    public static Invoice NewInvoice(Guid id, Guid workspaceId, ExtractedInvoice extracted, string xml, DateTimeOffset createdAt) => new(
        id, workspaceId, extracted.Supplier.Name, extracted.Supplier.TaxId, extracted.Customer.TaxId,
        extracted.IssueDate, extracted.Currency, extracted.Total, Hashing.RawXmlHash(xml), Hashing.InvoiceHash(extracted), extracted, createdAt);

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
        DateTimeOffset createdAt)
    {
        var extracted = invoice.Extracted;
        var category = bundle.CategoryOf(classification.CategoryId);
        var mode = transportMode ?? lane.ModeDefault;
        var massT = InvoiceFacts.MassTFromLines(extracted.Lines);
        var spend = Estimate.SpendBasedKg(extracted.Total, category.SectorKey, table);
        decimal? activity = status == NodeStatus.Estimated
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
            CreatedAt: createdAt);
    }
}
