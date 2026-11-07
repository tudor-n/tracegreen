using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Plausibility;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Trust;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Turns a stored node into the <see cref="NodeView"/> every client receives. The factor rows come from the seed's
/// factor table, the one the estimates are computed with, so each number is shown with the rows behind it.
/// Stored kilograms are not recomputed: after a <c>factors.json</c> change, nodes created before it show today's rows
/// until the demo is reset, and a row that no longer exists is left out rather than failing the whole graph.
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

        var spend = new CarbonFigure(node.SpendKg, Factors.SpendInputs(node.SectorId, invoice.IssueDate).ToList());
        var trust = TrustAssessor.Assess(node, invoice, lane, category, Factors);
        // F7: a verified node's activity figure is the counted one, so its rows are those of the state it is counted from
        // (an unbacked mode counts the lane's default mode's row). When no mass backs it, no activity figure counts and the
        // spend estimate stands.
        var counted = node.Status == NodeStatus.Verified ? trust?.CountedState : null;
        var activity = node.ActivityKg is { } activityKg && counted is not { MassT: null }
            ? new CarbonFigure(node.Status == NodeStatus.Verified ? node.CarbonKg : activityKg, Rows(
                (FactorKind.Transport, FactorTable.TransportKey(counted?.Mode ?? mode)),
                (FactorKind.Material, category.MaterialKey)))
            : null;
        var basis = node.Status == NodeStatus.Verified && activity is not null ? CarbonBasis.Activity : CarbonBasis.Spend;

        var line = InvoiceFacts.DominantLine(invoice.Extracted.Lines);
        var aiMapped = node.ClassificationSource is ClassificationSource.Cache or ClassificationSource.Model;
        var classification = new ClassificationView(
            CategoryId: node.CategoryId,
            CategoryLabel: category.Label,
            Source: node.ClassificationSource,
            Confidence: node.AiConfidence,
            AiMapped: aiMapped,
            LineDescription: line.Description,
            Review: node.Review,
            ReviewedAt: node.ReviewedAt,
            // No reason for UNCLASSIFIED (what is kept then is a rejection note, such as the model's out-of-list answer,
            // not a reason for a category) or after a buyer's review (the kept reason was for the category before it).
            Rationale: node.CategoryId == Ids.Unclassified || node.ClassificationSource == ClassificationSource.Buyer
                ? null
                : node.ClassificationRationale);

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

        var figures = new NodeFigures(
            EstimateKg: node.SpendKg,
            ClaimedKg: trust?.ClaimedKg,
            CountedKg: trust?.CountedKg,
            Target: TargetFigure.Compute(node, lane, category, _seed, Factors));

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
            Alternatives: ScenarioService.AlternativesFor(_seed, node.CategoryId).Select(Alternative).ToList(),
            MassT: node.MassT,
            TransportMode: node.TransportMode,
            EnergySource: node.EnergySource,
            SpendEur: node.SpendEur,
            CostEur: node.CostEur,
            LeadDays: node.LeadDays,
            Warnings: PlausibilityRules.For(node, InvoiceFacts.MassTFromLines(invoice.Extracted.Lines), lane),
            CreatedAt: node.CreatedAt,
            Figures: figures,
            Trust: trust?.Assessment,
            Evidence: EvidenceView.Of(node.Trust));
    }

    private AlternativeView Alternative(Alternative alternative)
    {
        var lane = _lanes[alternative.LaneId];
        return new AlternativeView(alternative.Id, alternative.Label, alternative.Country, lane.Origin, lane.ModeDefault, Illustrative: true);
    }

    private List<FactorLookup> Rows(params (FactorKind Kind, string Key)[] keys) =>
        keys.Select(k => Factors.TryLookup(k.Kind, k.Key)).OfType<FactorLookup>().ToList();
}
