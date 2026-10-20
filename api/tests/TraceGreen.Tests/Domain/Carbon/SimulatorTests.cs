using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Domain.Carbon;

public class SimulatorTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly FactorTable Table = FactorTable.FromSeed(Seed);
    private static readonly Lane CnRo = Seed.Lanes.Single(l => l.Id == "CN-RO");
    private const decimal EtsPrice = 70m;

    [Fact]
    public void N1AirToSeaIsWinWin()
    {
        var result = Simulator.ModeChange(TestNodes.N1(), CnRo, "zinc_diecast", TransportMode.Sea, Table, EtsPrice);

        Assert.Equal(7488.8m, result.CarbonKg);
        Assert.Equal(48690m, result.CostEur);
        Assert.Equal(40, result.LeadDays);
        Assert.Equal(-3910m, result.Delta.CostEur);
        Assert.Equal(33, result.Delta.LeadDays);
        Assert.Equal(MacBadge.WinWin, result.Badge);
        Assert.True(result.Illustrative);
    }

    [Fact]
    public void N1ToN1AltIsPremiumOrBelowEts()
    {
        var alt = Seed.Alternatives.Single(a => a.Id == "N1-alt");
        var altLane = Seed.Lanes.Single(l => l.Id == alt.LaneId);

        var result = Simulator.SupplierSwap(TestNodes.N1(), alt, altLane, "zinc_diecast", Table, EtsPrice);

        Assert.Equal(6941.4m, result.CarbonKg);
        Assert.Equal(57040m, result.CostEur);
        Assert.Equal(2, result.LeadDays);
        Assert.Equal(-5, result.Delta.LeadDays);
        Assert.Contains(result.Badge, new[] { MacBadge.Premium, MacBadge.BelowEts });
    }

    [Fact]
    public void LeadTimeDeltaComesFromLaneTable()
    {
        var rail = Simulator.ModeChange(TestNodes.N1(), CnRo, "zinc_diecast", TransportMode.Rail, Table, EtsPrice);

        Assert.Equal(20, rail.LeadDays);
        Assert.Equal(13, rail.Delta.LeadDays);
    }

    [Fact]
    public void ModeNotOfferedByTheLaneIsRejected() =>
        Assert.Throws<ModeNotAvailableException>(() =>
            Simulator.ModeChange(TestNodes.N1(), CnRo, "zinc_diecast", TransportMode.Road, Table, EtsPrice));

    [Fact]
    public void EstimatedNodeIsComparedAgainstItsSpendEstimate()
    {
        var estimated = TestNodes.N1(NodeStatus.Estimated, mode: null, energy: null);

        var result = Simulator.ModeChange(estimated, CnRo, "zinc_diecast", TransportMode.Sea, Table, EtsPrice);

        Assert.Equal(7488.8m - 21600m, result.Delta.CarbonKg);
    }
}
