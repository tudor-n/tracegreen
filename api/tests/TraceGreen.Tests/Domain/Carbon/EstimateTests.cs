using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Domain.Carbon;

public class EstimateTests
{
    private static readonly FactorTable Table = FactorTable.FromSeed(SeedLoader.Load(TestPaths.SeedDir));

    private static readonly ActivityInput N1Air = new(MassT: 2.3m, DistanceKm: 7500m, Mode: TransportMode.Air, MaterialKey: "zinc_diecast", Energy: EnergySource.Grid);

    [Fact]
    public void SpendBasedIsAmountTimesSectorFactor()
    {
        var result = Estimate.SpendBasedKg(48000m, "metals", Table);

        Assert.Equal(21600m, result.Kg);
        var input = Assert.Single(result.Inputs);
        Assert.Equal("kgCO2e/EUR", input.Unit);
    }

    [Fact]
    public void ActivityBasedMatchesHandComputationForN1Air()
    {
        var result = Estimate.ActivityBasedKg(N1Air, Table);

        Assert.Equal(13800m, result.TransportKg);
        Assert.Equal(6900m, result.MaterialKg);
        Assert.Equal(20700m, result.TotalKg);
        Assert.False(result.MassUnknown);
        Assert.Equal(2, result.Inputs.Count);
    }

    [Fact]
    public void RenewablePowerLowersActivityCarbonVsGrid()
    {
        var grid = Estimate.ActivityBasedKg(N1Air, Table);
        var renewable = Estimate.ActivityBasedKg(N1Air with { Energy = EnergySource.Renewable }, Table);

        Assert.True(renewable.TotalKg < grid.TotalKg);
        Assert.Equal(13800m + 6900m * (1 - 0.8m * 0.5m), renewable.TotalKg);
    }

    [Fact]
    public void UnknownEnergyIsTreatedLikeGrid()
    {
        var grid = Estimate.ActivityBasedKg(N1Air, Table);
        var unknown = Estimate.ActivityBasedKg(N1Air with { Energy = EnergySource.Unknown }, Table);
        var none = Estimate.ActivityBasedKg(N1Air with { Energy = null }, Table);

        Assert.Equal(grid.TotalKg, unknown.TotalKg);
        Assert.Equal(grid.TotalKg, none.TotalKg);
    }

    [Fact]
    public void UnknownMassGivesMassUnknownAndZeroTransportAndMaterial()
    {
        var result = Estimate.ActivityBasedKg(N1Air with { MassT = null }, Table);

        Assert.True(result.MassUnknown);
        Assert.Equal(0m, result.TransportKg);
        Assert.Equal(0m, result.MaterialKg);
        Assert.Equal(0m, result.TotalKg);
    }

    [Theory]
    [InlineData(TransportMode.Air, EnergySource.Grid, NodeStatus.Verified)]
    [InlineData(TransportMode.Air, null, NodeStatus.Partial)]
    [InlineData(null, EnergySource.Grid, NodeStatus.Partial)]
    [InlineData(null, null, NodeStatus.Estimated)]
    public void DeriveStatusFromAnswers(TransportMode? mode, EnergySource? energy, NodeStatus expected) =>
        Assert.Equal(expected, Estimate.DeriveStatus(mode, energy));
}
