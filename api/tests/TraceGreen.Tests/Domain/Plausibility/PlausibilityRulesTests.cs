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
}
