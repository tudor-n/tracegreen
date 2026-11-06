using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Domain.Carbon;

public class FactorTableTests
{
    private static readonly FactorTable Table = FactorTable.FromSeed(SeedLoader.Load(TestPaths.SeedDir));

    [Fact]
    public void LookupReturnsProvenance()
    {
        var lookup = Table.Lookup(FactorKind.Transport, "air");

        Assert.Equal(0.5313m, lookup.Value);
        Assert.Equal("kgCO2e/t.km", lookup.Unit);
        Assert.Equal(2026, lookup.Year);
        Assert.Equal("DESNZ UK GHG conversion factors", lookup.Source);
        Assert.Equal("Freighting goods: freight flights, international, to/from non-UK, without RF", lookup.RowLabel);
    }

    [Fact]
    public void LookupThrowsForUnknownKey() =>
        Assert.Throws<FactorNotFoundException>(() => Table.Lookup(FactorKind.Material, "unobtainium"));

    [Fact]
    public void TransportKeyIsLowerCaseModeName() =>
        Assert.Equal("sea", FactorTable.TransportKey(TransportMode.Sea));
}
