using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Carbon;

/// <summary>
/// What-if scenarios compared against the node's stored state. Pure: the caller resolves lanes, categories and factors.
/// Lane costs and lead times are illustrative and flagged as such on every result.
/// </summary>
public static class Simulator
{
    public static ScenarioResult ModeChange(
        SupplierNode node, Lane lane, string materialKey, TransportMode mode, FactorTable table, decimal etsPriceEurPerT)
    {
        var (distanceKm, leadDays, costPerKg) = Leg(lane, mode);
        var activity = Estimate.ActivityBasedKg(new ActivityInput(node.MassT, distanceKm, mode, materialKey, node.EnergySource), table);
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
