using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;

namespace TraceGreen.Tests.Domain.Carbon;

public class KpiCalculatorTests
{
    [Fact]
    public void PrimaryShareIsByEmissionsNotNodeCount()
    {
        var nodes = new[]
        {
            TestNodes.Simple("verified-big", NodeStatus.Verified, spendKg: 500m, activityKg: 90m, spendEur: 700m),
            TestNodes.Simple("estimated-small", NodeStatus.Estimated, spendKg: 10m, activityKg: null, spendEur: 300m),
        };

        var kpis = KpiCalculator.Compute(nodes);

        Assert.Equal(100m, kpis.TotalKg);
        Assert.Equal(0.9, kpis.PrimaryShare, precision: 9);
        Assert.Equal(0.1, kpis.EstimatedShare, precision: 9);
        Assert.Equal(1000m, kpis.SpendEur);
        Assert.Equal(2, kpis.NodeCount);
        Assert.Equal(1, kpis.VerifiedCount);
    }

    [Fact]
    public void PartialNodesCountAsEstimated()
    {
        var nodes = new[]
        {
            TestNodes.Simple("partial", NodeStatus.Partial, spendKg: 40m, activityKg: 5m),
            TestNodes.Simple("verified", NodeStatus.Verified, spendKg: 100m, activityKg: 60m),
        };

        var kpis = KpiCalculator.Compute(nodes);

        Assert.Equal(100m, kpis.TotalKg);
        Assert.Equal(0.6, kpis.PrimaryShare, precision: 9);
        Assert.Equal(1, kpis.VerifiedCount);
    }

    [Fact]
    public void EmptyGraphGivesZeroKpisWithoutNaN()
    {
        var kpis = KpiCalculator.Compute([]);

        Assert.Equal(0m, kpis.TotalKg);
        Assert.Equal(0, kpis.PrimaryShare);
        Assert.Equal(0, kpis.EstimatedShare);
        Assert.False(double.IsNaN(kpis.PrimaryShare));
        Assert.Equal(0, kpis.NodeCount);
    }
}
