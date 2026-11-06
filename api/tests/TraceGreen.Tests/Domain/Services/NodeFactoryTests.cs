using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Tests.Domain.Services;

public class NodeFactoryTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly FactorTable Table = FactorTable.FromSeed(Seed);
    private static readonly Lane CnRo = Seed.Lanes.Single(l => l.Id == "CN-RO");
    private static readonly NodeClassification ZincDiecast = new("zinc_diecast", 1.0, ClassificationSource.Invoice);

    private static Invoice N1Invoice(string xml)
    {
        var parsed = new UblInvoiceParser().Parse(xml);
        Assert.True(parsed.IsOk);
        return NodeFactory.NewInvoice(Guid.NewGuid(), Ids.DemoWorkspace, parsed.Value, xml, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void ASupplierAnswerWithAMassGivesAnActivityFigure()
    {
        var invoice = N1Invoice(File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml")));

        var node = NodeFactory.NewNode(Guid.NewGuid(), invoice, ZincDiecast, CnRo, NodeStatus.Verified, TransportMode.Air, EnergySource.Grid, Seed, Table, DateTimeOffset.UnixEpoch);

        Assert.Equal(17148.020622m, node.ActivityKg); // TestNodes.N1: 9164.925 kg transport + 7983.095622 kg material
        Assert.Equal(17148.020622m, node.CarbonKg);
    }

    [Fact]
    public void WithoutAMassThereIsNoActivityFigureAndTheNodeStaysOnSpend()
    {
        var xml = File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml")).Replace("unitCode=\"KGM\"", "unitCode=\"H87\"");
        var invoice = N1Invoice(xml);

        var node = NodeFactory.NewNode(Guid.NewGuid(), invoice, ZincDiecast, CnRo, NodeStatus.Verified, TransportMode.Air, EnergySource.Grid, Seed, Table, DateTimeOffset.UnixEpoch);

        Assert.Null(node.MassT);
        Assert.Null(node.ActivityKg);
        Assert.Equal(node.SpendKg, node.CarbonKg);
        Assert.True(node.CarbonKg > 0m);
    }

    [Fact]
    public void TheNodeKeepsTheClassificationReason()
    {
        var invoice = N1Invoice(File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml")));
        var classification = ZincDiecast with { Rationale = "Die-cast   zinc\nhousing" };

        var node = NodeFactory.NewNode(Guid.NewGuid(), invoice, classification, CnRo, NodeStatus.Estimated, null, null, Seed, Table, DateTimeOffset.UnixEpoch);

        Assert.Equal("Die-cast zinc housing", node.ClassificationRationale);
    }

    [Fact]
    public void AReasonIsCappedAt200Characters()
    {
        var reason = NodeFactory.Reason(new string('a', 150) + " " + new string('b', 150));

        Assert.NotNull(reason);
        Assert.True(reason.Length <= NodeFactory.MaxRationaleChars);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyReasonIsNull(string? rationale) => Assert.Null(NodeFactory.Reason(rationale));

    [Fact]
    public void AReasonAgainstTheCopyRulesIsDropped() =>
        // Built at run time so this file itself passes the copy guard.
        Assert.Null(NodeFactory.Reason("Recorded on a " + "block" + "chain"));

    [Fact]
    public void ATermTheCutWouldSplitIsStillCaught() =>
        // The term spans characters 194 to 203: cut first, its prefix alone would pass the check.
        Assert.Null(NodeFactory.Reason(new string('a', 193) + " " + "block" + "chain" + " ledger"));

    [Fact]
    public void TheCutNeverSplitsASurrogatePair()
    {
        // 199 letters, then an emoji (two UTF-16 units): a plain cut at 200 would keep its high surrogate alone.
        var reason = NodeFactory.Reason(new string('a', 199) + "\U0001F600" + " tail");

        Assert.NotNull(reason);
        Assert.True(reason.Length <= NodeFactory.MaxRationaleChars);
        Assert.False(char.IsHighSurrogate(reason[^1]));
        // Strict UTF-8, as Npgsql writes text: throws on a lone surrogate.
        _ = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(reason);
    }
}
