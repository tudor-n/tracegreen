using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Trust;

/// <summary>
/// The best state a delivery could reach, the third figure on a node next to the estimate and the supplier data. By default
/// the lowest-emission mode the lane has and renewable power; the buyer can choose a mode, a power source or one of the
/// node's alternative suppliers instead (<c>PUT /api/nodes/{id}/target</c>). Computed with the activity formula the
/// simulator uses; like every lane figure it is illustrative.
/// </summary>
public sealed record TargetView(
    decimal Kg,
    TransportMode Mode,
    EnergySource Energy,
    string? AlternativeId,
    string? AlternativeLabel,
    bool IsDefault,
    bool Illustrative);

public static class TargetFigure
{
    /// <summary>Null when the delivery's mass is unknown (and no alternative with a mass is chosen): no activity figure exists.</summary>
    public static TargetView? Compute(SupplierNode node, Lane lane, Category category, SeedBundle seed, FactorTable table)
    {
        var choice = node.Target;
        var energy = choice?.Energy ?? EnergySource.Renewable;
        if (choice?.AlternativeId is { } alternativeId
            && seed.Alternatives.FirstOrDefault(a => a.Id == alternativeId && a.NodeCategory == node.CategoryId) is { } alternative
            && seed.Lanes.FirstOrDefault(l => l.Id == alternative.LaneId) is { } alternativeLane)
        {
            var altModes = Simulator.AvailableModes(alternativeLane);
            var altMode = choice.Mode is { } m && altModes.Contains(m) ? m : alternativeLane.ModeDefault;
            return new TargetView(
                Kg(alternative.MassT, alternativeLane, altMode, category.MaterialKey, energy, table),
                altMode, energy, alternative.Id, alternative.Label, IsDefault: false, Illustrative: true);
        }
        if (node.MassT is not { } massT)
        {
            return null;
        }
        var modes = Simulator.AvailableModes(lane);
        var mode = choice?.Mode is { } chosen && modes.Contains(chosen)
            ? chosen
            : modes.MinBy(m => (lane.DistanceKm.GetValueOrDefault(m) ?? 0m) * table.Transport(m).Value);
        return new TargetView(
            Kg(massT, lane, mode, category.MaterialKey, energy, table), mode, energy, null, null,
            IsDefault: choice is null || choice.IsEmpty, Illustrative: true);
    }

    /// <summary>Why <paramref name="choice"/> cannot be the node's target, or null when it can.</summary>
    public static (TargetProblem Problem, string Message)? Problem(SupplierNode node, Lane lane, TargetChoice choice, SeedBundle seed)
    {
        if (choice.AlternativeId is { } alternativeId)
        {
            var alternative = seed.Alternatives.FirstOrDefault(a => a.Id == alternativeId);
            if (alternative is null)
            {
                return (TargetProblem.AlternativeNotFound, $"No alternative {alternativeId}.");
            }
            if (alternative.NodeCategory != node.CategoryId)
            {
                return (TargetProblem.InvalidChange, $"Alternative {alternativeId} is for {alternative.NodeCategory}, not {node.CategoryId}.");
            }
            var alternativeLane = seed.Lanes.First(l => l.Id == alternative.LaneId);
            if (choice.Mode is { } m && !Simulator.AvailableModes(alternativeLane).Contains(m))
            {
                return (TargetProblem.ModeNotAvailable, $"Lane {alternativeLane.Id} has no {TrustAssessor.Name(m)} leg.");
            }
            return null;
        }
        if (choice.Mode is { } mode && !Simulator.AvailableModes(lane).Contains(mode))
        {
            return (TargetProblem.ModeNotAvailable, $"Lane {lane.Id} has no {TrustAssessor.Name(mode)} leg.");
        }
        if (node.MassT is null && !choice.IsEmpty)
        {
            return (TargetProblem.MassUnknown, "The delivery's mass is unknown, so no target can be computed for it.");
        }
        return null;
    }

    private static decimal Kg(decimal massT, Lane lane, TransportMode mode, string materialKey, EnergySource energy, FactorTable table) =>
        Estimate.ActivityBasedKg(new ActivityInput(massT, lane.DistanceKm.GetValueOrDefault(mode) ?? 0m, mode, materialKey, energy), table).TotalKg;
}

public enum TargetProblem { AlternativeNotFound, InvalidChange, ModeNotAvailable, MassUnknown }
