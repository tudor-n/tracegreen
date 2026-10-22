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

        Assert.Equal(20700m, node.ActivityKg);
        Assert.Equal(20700m, node.CarbonKg);
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
}
