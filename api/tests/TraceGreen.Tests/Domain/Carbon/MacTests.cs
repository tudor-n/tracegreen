using System.Text.Json;
using TraceGreen.Domain.Carbon;

namespace TraceGreen.Tests.Domain.Carbon;

public class MacTests
{
    private const decimal EtsPrice = 70m;

    [Fact]
    public void CheaperAndCleanerIsWinWin()
    {
        var mac = Mac.Compute(costOldEur: 100m, costNewEur: 90m, carbonOldKg: 2000m, carbonNewKg: 1000m);

        Assert.Equal(MacKind.Abatement, mac.Kind);
        Assert.Equal(-10m, mac.MacEurPerT);
        Assert.Equal(-10m, mac.DeltaCostEur);
        Assert.Equal(1m, mac.DeltaCarbonT);
        Assert.Equal(MacBadge.WinWin, Mac.Badge(mac, EtsPrice));
    }

    [Fact]
    public void CostBelowCarbonPriceIsBelowEts()
    {
        var mac = Mac.Compute(100m, 150m, 2000m, 1000m);

        Assert.Equal(50m, mac.MacEurPerT);
        Assert.Equal(MacBadge.BelowEts, Mac.Badge(mac, EtsPrice));
    }

    [Fact]
    public void CostAtOrAboveCarbonPriceIsPremium()
    {
        Assert.Equal(MacBadge.Premium, Mac.Badge(Mac.Compute(100m, 170m, 2000m, 1000m), EtsPrice));
        Assert.Equal(MacBadge.Premium, Mac.Badge(Mac.Compute(100m, 300m, 2000m, 1000m), EtsPrice));
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(1000, 1500)]
    public void ZeroOrNegativeCarbonSavingIsNotAbatementWithoutInfinity(decimal carbonOld, decimal carbonNew)
    {
        var mac = Mac.Compute(1m, 2m, carbonOld, carbonNew);

        Assert.Equal(MacKind.NotAbatement, mac.Kind);
        Assert.Null(mac.MacEurPerT);
        Assert.Equal(MacBadge.NotAbatement, Mac.Badge(mac, EtsPrice));
        var json = JsonSerializer.Serialize(mac);
        Assert.DoesNotContain("Infinity", json);
        Assert.DoesNotContain("NaN", json);
    }

    [Fact]
    public void MacIsRoundedToCents() =>
        Assert.Equal(33.33m, Mac.Compute(0m, 100m, 3000m, 0m).MacEurPerT);
}
