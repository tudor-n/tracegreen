using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;
using TraceGreen.Domain.Trust;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Tests.Domain.Trust;

public class TargetFigureTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly FactorTable Table = FactorTable.FromSeed(Seed);
    private static readonly Lane CnRo = Seed.Lanes.Single(l => l.Id == "CN-RO");
    private static readonly Category ZincDiecast = Seed.CategoryOf("zinc_diecast");

    private static SupplierNode N1(TargetChoice? target = null)
    {
        var xml = File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml"));
        var invoice = NodeFactory.NewInvoice(Guid.NewGuid(), Ids.DemoWorkspace, new UblInvoiceParser().Parse(xml).Value, xml, DateTimeOffset.UnixEpoch);
        var node = NodeFactory.NewNode(
            Guid.NewGuid(), invoice, new NodeClassification("zinc_diecast", 1.0, ClassificationSource.Invoice), CnRo,
            NodeStatus.Estimated, null, null, Seed, Table, DateTimeOffset.UnixEpoch);
        return node with { Target = target };
    }

    [Fact]
    public void TheDefaultIsTheLanesLowestEmissionModeWithRenewablePower()
    {
        var target = TargetFigure.Compute(N1(), CnRo, ZincDiecast, Seed, Table)!;

        // Per tonne: sea 16 000 × 0.01612 = 257.92 kg, rail 11 000 × 0.02583 = 284.13 kg, air 3 984.75 kg.
        Assert.Equal(TransportMode.Sea, target.Mode);
        Assert.Equal(EnergySource.Renewable, target.Energy);
        Assert.Equal(593.216m + 7983.095622m * 0.6m, target.Kg);
        Assert.True(target.IsDefault);
        Assert.True(target.Illustrative);
    }

    [Fact]
    public void TheBuyerCanChooseTheModeAndThePower()
    {
        var target = TargetFigure.Compute(N1(new TargetChoice(TransportMode.Rail, EnergySource.Grid)), CnRo, ZincDiecast, Seed, Table)!;

        Assert.Equal(TransportMode.Rail, target.Mode);
        Assert.Equal(EnergySource.Grid, target.Energy);
        Assert.Equal(2.3m * 284.13m + 7983.095622m, target.Kg);
        Assert.False(target.IsDefault);
    }

    [Fact]
    public void AnAlternativeSupplierUsesItsOwnLaneAndMass()
    {
        var alternative = Seed.Alternatives.First(a => a.NodeCategory == "zinc_diecast");
        var target = TargetFigure.Compute(N1(new TargetChoice(AlternativeId: alternative.Id)), CnRo, ZincDiecast, Seed, Table)!;

        Assert.Equal(alternative.Id, target.AlternativeId);
        Assert.Equal(alternative.Label, target.AlternativeLabel);
        Assert.Equal(Seed.Lanes.Single(l => l.Id == alternative.LaneId).ModeDefault, target.Mode);
    }

    [Fact]
    public void AChoiceTheLaneCannotTakeIsRefused()
    {
        var node = N1();
        Assert.Equal(TargetProblem.ModeNotAvailable, TargetFigure.Problem(node, CnRo, new TargetChoice(TransportMode.Road), Seed)?.Problem);
        Assert.Equal(TargetProblem.AlternativeNotFound, TargetFigure.Problem(node, CnRo, new TargetChoice(AlternativeId: "nope"), Seed)?.Problem);
        var otherCategory = Seed.Alternatives.First(a => a.NodeCategory != "zinc_diecast");
        Assert.Equal(TargetProblem.InvalidChange, TargetFigure.Problem(node, CnRo, new TargetChoice(AlternativeId: otherCategory.Id), Seed)?.Problem);
        Assert.Null(TargetFigure.Problem(node, CnRo, new TargetChoice(TransportMode.Rail), Seed));
    }
}
