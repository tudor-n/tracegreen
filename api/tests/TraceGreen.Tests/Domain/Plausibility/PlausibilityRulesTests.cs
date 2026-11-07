using TraceGreen.Domain.Model;
using TraceGreen.Domain.Plausibility;

namespace TraceGreen.Tests.Domain.Plausibility;

public class PlausibilityRulesTests
{
    [Fact]
    public void AirAboveTwoTonnesWarns()
    {
        // N1 in the demo: 2.3 t confirmed by air.
        var warning = Assert.Single(PlausibilityRules.Check(TransportMode.Air, 2.3m));

        Assert.Equal(PlausibilityCode.AirHeavy, warning.Code);
        Assert.Equal("Air freight confirmed for 2.3 t; more than 2 t by air is unusual.", warning.Detail);
    }

    [Theory]
    [InlineData(TransportMode.Air, 2.0)]
    [InlineData(TransportMode.Air, 0.5)]
    [InlineData(TransportMode.Sea, 2.3)]
    [InlineData(TransportMode.Road, 40.0)]
    public void OtherAnswersDoNotWarn(TransportMode mode, double massT) =>
        Assert.Empty(PlausibilityRules.Check(mode, (decimal)massT));

    [Fact]
    public void UnconfirmedModeOrUnknownMassDoesNotWarn()
    {
        Assert.Empty(PlausibilityRules.Check(null, 2.3m));
        Assert.Empty(PlausibilityRules.Check(TransportMode.Air, null));
    }

    [Fact]
    public void SeaReceivedInUnderHalfItsLeadTimeWarns()
    {
        var warning = Assert.Single(PlausibilityRules.Check(TransportMode.Sea, 2.3m, invoiceMassT: 2.3m, transitDays: 9, seaLeadDays: 40));

        Assert.Equal(PlausibilityCode.SeaTooFast, warning.Code);
        Assert.Equal("Sea freight confirmed, but received 9 days after dispatch; sea takes about 40 days on this lane.", warning.Detail);
        Assert.Empty(PlausibilityRules.Check(TransportMode.Sea, 2.3m, 2.3m, transitDays: 20, seaLeadDays: 40));
        Assert.Empty(PlausibilityRules.Check(TransportMode.Sea, 2.3m, 2.3m, transitDays: null, seaLeadDays: 40));
        Assert.Empty(PlausibilityRules.Check(TransportMode.Rail, 2.3m, 2.3m, transitDays: 9, seaLeadDays: 40));
    }

    [Fact]
    public void AShippingMassBelowTheInvoicesGoodsWarns()
    {
        var warning = Assert.Single(PlausibilityRules.Check(TransportMode.Road, 1.5m, invoiceMassT: 2m, transitDays: null, seaLeadDays: null));

        Assert.Equal(PlausibilityCode.MassBelowInvoice, warning.Code);
        Assert.Equal("1.5 t confirmed is less than the invoice's 2 t of goods.", warning.Detail);
        Assert.Empty(PlausibilityRules.Check(TransportMode.Road, 2.5m, invoiceMassT: 2m, transitDays: null, seaLeadDays: null));
    }
}
