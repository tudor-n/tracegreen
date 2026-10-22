using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Turns a stored node into the <see cref="NodeView"/> every client receives. The factor rows come from the seed's
/// factor table, the one the estimates are computed with, so each number is shown with the rows behind it.
/// </summary>
public sealed class NodeViewMapper
{
    private readonly SeedBundle _seed;
    private readonly Dictionary<string, Lane> _lanes;

    public NodeViewMapper(SeedBundle seed)
    {
        _seed = seed;
        Factors = FactorTable.FromSeed(seed);
        _lanes = seed.Lanes.ToDictionary(l => l.Id, StringComparer.Ordinal);
    }

    public FactorTable Factors { get; }

    public NodeView Map(SupplierNode node, Invoice invoice)
    {
        var category = _seed.CategoryOf(node.CategoryId);
        var lane = _lanes.GetValueOrDefault(node.LaneId) ?? throw new InvalidOperationException($"Node {node.Id} has unknown lane {node.LaneId}.");
        var mode = node.TransportMode ?? lane.ModeDefault;

        var spend = new CarbonFigure(node.SpendKg, [Factors.Lookup(FactorKind.Spend, node.SectorId)]);
        var activity = node.ActivityKg is { } activityKg
            ? new CarbonFigure(activityKg, [Factors.Transport(mode), Factors.Lookup(FactorKind.Material, category.MaterialKey)])
            : null;
        var basis = node.Status == NodeStatus.Verified && activity is not null ? CarbonBasis.Activity : CarbonBasis.Spend;

        var line = InvoiceFacts.DominantLine(invoice.Extracted.Lines);
        var classification = new ClassificationView(
            CategoryId: node.CategoryId,
            CategoryLabel: category.Label,
            Source: node.ClassificationSource,
            Confidence: node.AiConfidence,
            AiMapped: node.ClassificationSource is ClassificationSource.Cache or ClassificationSource.Model,
            LineDescription: line.Description);

        // Every lane is illustrative until a sourced lane table exists (CLAUDE.md: lane costs and lead times are labelled).
        var laneView = new LaneView(
            Id: lane.Id,
            Origin: lane.Origin,
            Dest: lane.Dest,
            Mode: mode,
            ModeDefault: lane.ModeDefault,
            Modes: Simulator.AvailableModes(lane),
            DistanceKm: lane.DistanceKm.GetValueOrDefault(mode),
            Illustrative: true);

        return new NodeView(
            Id: node.Id,
            Label: node.Label,
            Country: node.Country,
            Status: node.Status,
            CarbonKg: node.CarbonKg,
            Basis: basis,
            Uncertainty: Estimate.DeriveUncertainty(node.Status, node.EnergySource, node.MassT),
            Spend: spend,
            Activity: activity,
            Classification: classification,
            Invoice: new InvoiceSummary(invoice.Id, invoice.Extracted.Number, invoice.IssueDate, invoice.Currency, invoice.TotalAmount),
            Lane: laneView,
            MassT: node.MassT,
            TransportMode: node.TransportMode,
            EnergySource: node.EnergySource,
            SpendEur: node.SpendEur,
            CostEur: node.CostEur,
            LeadDays: node.LeadDays,
            CreatedAt: node.CreatedAt);
    }
}
