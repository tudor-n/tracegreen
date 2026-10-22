using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Tests.Domain.Services;

public class NodeViewMapperTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly DemoStateData State = DemoState.Build(Seed);
    private static readonly NodeViewMapper Mapper = new(Seed);

    private static NodeView View(string key)
    {
        var spec = Seed.DemoState.Nodes.Single(n => n.Key == key);
        var node = State.Nodes.Single(n => n.Id == spec.NodeId);
        return Mapper.Map(node, State.Invoices.Single(i => i.Id == node.InvoiceId));
    }

    [Fact]
    public void VerifiedNodeShowsActivityFigureWithTransportAndMaterialRows()
    {
        var n2 = View("N2");

        Assert.Equal(NodeStatus.Verified, n2.Status);
        Assert.Equal(CarbonBasis.Activity, n2.Basis);
        Assert.Equal(n2.Activity!.Kg, n2.CarbonKg);
        Assert.Equal(["Sea container freight (placeholder)", "Electrical items: printed circuit boards (placeholder)"], n2.Activity.Factors.Select(f => f.RowLabel));
        Assert.Equal(Uncertainty.Low, n2.Uncertainty);
        Assert.Equal(TransportMode.Sea, n2.Lane.Mode);
    }

    [Fact]
    public void EstimatedNodeShowsSpendFigureAndHighUncertainty()
    {
        var n3 = View("N3");

        Assert.Equal(CarbonBasis.Spend, n3.Basis);
        Assert.Equal(n3.Spend.Kg, n3.CarbonKg);
        Assert.Null(n3.Activity);
        Assert.Equal(Uncertainty.High, n3.Uncertainty);
        var factor = Assert.Single(n3.Spend.Factors);
        Assert.Equal("kgCO2e/EUR", factor.Unit);
    }

    [Fact]
    public void PartialNodeStaysOnTheSpendFigure()
    {
        var n5 = View("N5");

        Assert.Equal(NodeStatus.Partial, n5.Status);
        Assert.Equal(CarbonBasis.Spend, n5.Basis);
        Assert.Equal(n5.Spend.Kg, n5.CarbonKg);
        Assert.Equal(Uncertainty.High, n5.Uncertainty);
    }

    [Fact]
    public void PlaceholderSourcesReachTheView()
    {
        var n3 = View("N3");

        Assert.Contains("PLACEHOLDER", n3.Spend.Factors[0].Source);
    }

    [Fact]
    public void CacheClassificationIsAiMappedAndInvoiceCodeIsNot()
    {
        var n3 = View("N3");
        var n2 = View("N2");

        Assert.Equal(ClassificationSource.Cache, n3.Classification.Source);
        Assert.True(n3.Classification.AiMapped);
        Assert.Equal(ClassificationSource.Invoice, n2.Classification.Source);
        Assert.False(n2.Classification.AiMapped);
        Assert.Equal("Printed circuit boards", n2.Classification.CategoryLabel);
        Assert.False(string.IsNullOrWhiteSpace(n2.Classification.LineDescription));
    }

    [Fact]
    public void LaneIsIllustrativeAndListsItsModes()
    {
        var n5 = View("N5");

        Assert.True(n5.Lane.Illustrative);
        Assert.Equal("DE-HH-SB", n5.Lane.Id);
        Assert.Equal([TransportMode.Road, TransportMode.Rail], n5.Lane.Modes);
        Assert.Equal(1500m, n5.Lane.DistanceKm);
    }

    [Fact]
    public void InvoiceSummaryComesFromTheInvoice()
    {
        var n2 = View("N2");
        var invoice = State.Invoices.Single(i => i.Id == n2.Invoice.Id);

        Assert.Equal(invoice.Extracted.Number, n2.Invoice.Number);
        Assert.Equal(invoice.TotalAmount, n2.Invoice.Total);
    }
}
