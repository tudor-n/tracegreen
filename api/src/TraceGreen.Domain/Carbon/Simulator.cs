using TraceGreen.Domain.Model;
using TraceGreen.Domain.Trust;

namespace TraceGreen.Domain.Carbon;

/// <summary>
/// What-if scenarios compared against the node's stored state. Pure: the caller resolves lanes, categories and factors.
/// Lane costs and lead times are illustrative and flagged as such on every result.
/// </summary>
public static class Simulator
{
    /// <param name="counted">
    /// The answers the node's counted figure rests on (F7: each one nothing backs yet replaced by its fallback). The scenario
    /// keeps them and changes the mode alone, so its delta compares like with like; null uses the node's answers as given.
    /// </param>
    public static ScenarioResult ModeChange(
        SupplierNode node, Lane lane, string materialKey, TransportMode mode, FactorTable table, decimal etsPriceEurPerT,
        ActivityState? counted = null)
    {
        var (distanceKm, leadDays, costPerKg) = Leg(lane, mode);
        var energy = counted is null ? node.EnergySource : counted.Energy;
        var massT = counted is null ? node.MassT : counted.MassT;
        var activity = Estimate.ActivityBasedKg(new ActivityInput(massT, distanceKm, mode, materialKey, energy), table);
        var costEur = node.SpendEur + (node.MassT ?? 0m) * 1000m * costPerKg;
        return Build(node, activity, costEur, leadDays, etsPriceEurPerT);
    }

    public static ScenarioResult SupplierSwap(
        SupplierNode node, Alternative alternative, Lane alternativeLane, string alternativeMaterialKey, FactorTable table, decimal etsPriceEurPerT)
    {
        var mode = alternativeLane.ModeDefault;
        var (distanceKm, leadDays, costPerKg) = Leg(alternativeLane, mode);
        var activity = Estimate.ActivityBasedKg(
            new ActivityInput(alternative.MassT, distanceKm, mode, alternativeMaterialKey, EnergySource.Grid), table);
        var costEur = alternative.CostEur + alternative.MassT * 1000m * costPerKg;
        return Build(node, activity, costEur, leadDays, etsPriceEurPerT);
    }

    /// <summary>Modes the lane has a distance, a lead time and a cost for: the ones a scenario or a supplier may pick.</summary>
    public static IReadOnlyList<TransportMode> AvailableModes(Lane lane) =>
        Enum.GetValues<TransportMode>()
            .Where(m => lane.DistanceKm.GetValueOrDefault(m) is not null
                && lane.LeadDaysByMode.GetValueOrDefault(m) is not null
                && lane.CostPerKgByMode.GetValueOrDefault(m) is not null)
            .ToList();

    private static (decimal DistanceKm, int LeadDays, decimal CostPerKg) Leg(Lane lane, TransportMode mode)
    {
        var distance = lane.DistanceKm.GetValueOrDefault(mode) ?? throw new ModeNotAvailableException(lane.Id, mode);
        var lead = lane.LeadDaysByMode.GetValueOrDefault(mode) ?? throw new ModeNotAvailableException(lane.Id, mode);
        var cost = lane.CostPerKgByMode.GetValueOrDefault(mode) ?? throw new ModeNotAvailableException(lane.Id, mode);
        return (distance, lead, cost);
    }

    private static ScenarioResult Build(SupplierNode baseline, ActivityResult activity, decimal costEur, int leadDays, decimal etsPriceEurPerT)
    {
        var mac = Mac.Compute(baseline.CostEur, costEur, baseline.CarbonKg, activity.TotalKg);
        var delta = new ScenarioDelta(activity.TotalKg - baseline.CarbonKg, costEur - baseline.CostEur, leadDays - baseline.LeadDays);
        return new ScenarioResult(
            CarbonKg: activity.TotalKg,
            CostEur: costEur,
            LeadDays: leadDays,
            Delta: delta,
            Mac: mac,
            Badge: Mac.Badge(mac, etsPriceEurPerT),
            Inputs: activity.Inputs,
            MassUnknown: activity.MassUnknown,
            Illustrative: true);
    }
}
