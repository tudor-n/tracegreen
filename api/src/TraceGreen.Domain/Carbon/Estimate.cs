using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Carbon;

public sealed record SpendResult(decimal Kg, IReadOnlyList<FactorLookup> Inputs);

public sealed record ActivityInput(decimal? MassT, decimal DistanceKm, TransportMode Mode, string MaterialKey, EnergySource? Energy);

public sealed record ActivityResult(decimal TransportKg, decimal MaterialKg, decimal TotalKg, bool MassUnknown, IReadOnlyList<FactorLookup> Inputs);

/// <summary>
/// Deterministic carbon estimates. Spend-based (amber) before the supplier answers,
/// activity-based (green) after: transport + material × (1 − renewable share × process electricity share).
/// </summary>
public static class Estimate
{
    /// <summary>Share of material-stage emissions attributed to electricity; a documented simplification.</summary>
    public const decimal ProcessElectricityShare = 0.5m;

    public static decimal RenewableShare(EnergySource? energy) => energy == EnergySource.Renewable ? 0.8m : 0m;

    public static SpendResult SpendBasedKg(decimal amountEur, string sectorKey, FactorTable table)
    {
        var factor = table.Lookup(FactorKind.Spend, sectorKey);
        return new SpendResult(amountEur * factor.Value, [factor]);
    }

    public static ActivityResult ActivityBasedKg(ActivityInput input, FactorTable table)
    {
        var transportFactor = table.Transport(input.Mode);
        var materialFactor = table.Lookup(FactorKind.Material, input.MaterialKey);
        var inputs = new[] { transportFactor, materialFactor };
        if (input.MassT is not { } massT)
        {
            return new ActivityResult(0m, 0m, 0m, MassUnknown: true, inputs);
        }

        var transportKg = massT * input.DistanceKm * transportFactor.Value;
        var materialKg = massT * materialFactor.Value;
        var totalKg = transportKg + materialKg * (1 - RenewableShare(input.Energy) * ProcessElectricityShare);
        return new ActivityResult(transportKg, materialKg, totalKg, MassUnknown: false, inputs);
    }

    public static NodeStatus DeriveStatus(TransportMode? mode, EnergySource? energy) => (mode, energy) switch
    {
        (not null, not null) => NodeStatus.Verified,
        (null, null) => NodeStatus.Estimated,
        _ => NodeStatus.Partial,
    };
}
