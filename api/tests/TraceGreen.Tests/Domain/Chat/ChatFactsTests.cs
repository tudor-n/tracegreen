using System.Globalization;
using System.Text.Json.Nodes;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Chat;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Tests.Domain.Chat;

/// <summary>The chat's fact pack and the rules its answers must pass (F13), on the seed's demo state.</summary>
public class ChatFactsTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly DemoStateData State = DemoState.Build(Seed);
    private static readonly NodeViewMapper Mapper = new(Seed);

    private static GraphSnapshot Snapshot()
    {
        var views = State.Nodes.Select(n => Mapper.Map(n, State.Invoices.Single(i => i.Id == n.InvoiceId))).ToList();
        var audit = new AuditEntry(7, Ids.DemoWorkspace, new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero), "buyer", "invoice.imported",
            new JsonObject(), new string('0', 64), new string('a', 64));
        return new GraphSnapshot(Seed.Buyer, views, KpiCalculator.Compute(State.Nodes), [audit], 0, "memory");
    }

    private static readonly FactPack Pack = FactPack.Build(Snapshot(), []);

    private static Fact F(string id) => Pack.Find(id) ?? throw new InvalidOperationException($"no fact {id}");

    [Fact]
    public void SuppliersAreNumberedByEmissionsSoS1IsTheLargest()
    {
        var largest = State.Nodes.MaxBy(n => n.CarbonKg)!;

        Assert.Equal(largest.Label, F("S1.name").Value);
        Assert.Equal(largest.CarbonKg.ToString(CultureInfo.InvariantCulture), F("S1.carbon").Value);
        Assert.Equal(FactKind.CarbonKg, F("S1.carbon").Kind);
        Assert.Equal(largest.Id, F("S1.carbon").NodeId);
        Assert.Contains("the largest emitter", F("S1.carbon").About);
        var carbons = Enumerable.Range(1, State.Nodes.Count).Select(i => decimal.Parse(F($"S{i}.carbon").Value, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(carbons.OrderByDescending(c => c), carbons);
    }

    [Fact]
    public void TotalsCountsAndTheConfirmedShareAreTheApisOwn()
    {
        var kpis = KpiCalculator.Compute(State.Nodes);

        Assert.Equal(kpis.TotalKg.ToString(CultureInfo.InvariantCulture), F("total.carbon").Value);
        Assert.Equal(kpis.NodeCount.ToString(CultureInfo.InvariantCulture), F("count.deliveries").Value);
        Assert.Equal(State.Nodes.Count(n => n.Status == NodeStatus.Verified).ToString(CultureInfo.InvariantCulture), F("count.confirmed").Value);
        Assert.Equal(FactKind.Share, F("share.confirmed").Kind);
        Assert.Contains("by emissions, not by node count", F("share.confirmed").About);
    }

    [Fact]
    public void EveryNodeFactSaysWhichSupplierItIsAboutAndLinksToIt()
    {
        Assert.All(Pack.Facts.Where(f => f.Id.StartsWith('S')), f =>
        {
            Assert.NotNull(f.Subject);
            Assert.NotNull(f.NodeId);
        });
        Assert.Equal(7, F("A1.action").AuditId);
    }

    [Fact]
    public void TheProvenanceIsRulingThreesLine()
    {
        var verified = Enumerable.Range(1, State.Nodes.Count).Select(i => $"S{i}").First(s => F($"{s}.status").Value == "VERIFIED");

        Assert.StartsWith("Supplier-confirmed · ", F($"{verified}.basis").Value);
        var estimated = Enumerable.Range(1, State.Nodes.Count).Select(i => $"S{i}").First(s => F($"{s}.status").Value == "ESTIMATED");
        Assert.StartsWith("Spend-based estimate · ", F($"{estimated}.basis").Value);
    }

    [Fact]
    public void AnAnswerThatCitesFactsPassesAndIsCutIntoSegments()
    {
        var result = AnswerCheck.Check("The largest emitter is {{S1.name}} with {{S1.carbon}} of CO2e, {{S1.basis}}.", Pack);

        Assert.True(result.Ok, result.Problem);
        Assert.Equal(["S1.name", "S1.carbon", "S1.basis"], result.FactIds);
        Assert.Equal("The largest emitter is ", result.Segments[0].Text);
        Assert.Equal("S1.name", result.Segments[1].FactId);
        Assert.Equal(".", result.Segments[^1].Text);
    }

    [Theory]
    [InlineData("The total is 21.6 t.", "a number written out")]
    [InlineData("{{S1.name}} emits 3 times more than {{S2.name}}.", "a number written out")]
    [InlineData("The largest is {{S9.name}}.", "unknown fact S9.name")]
    [InlineData("The largest is {{S1.invented}}.", "unknown fact S1.invented")]
    [InlineData("Your largest supplier is Taiwan PCB, {{S1.carbon}}.", "a name written out")]
    [InlineData("Try Cluj Precision SRL instead of {{S1.name}}.", "a company name written out")]
    [InlineData("Try Brasov Castings GmbH, cheaper than {{S1.name}}.", "a company name written out")]
    [InlineData("Most of it is estimated.", "no fact cited")]
    [InlineData("", "empty answer")]
    [InlineData("{{S1.name}} is {S1.status}}.", "malformed placeholder")]
    public void AnswersThatBreakARuleAreRefused(string text, string problem)
    {
        var result = AnswerCheck.Check(text, Pack);

        Assert.False(result.Ok);
        Assert.Equal(problem, result.Problem);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public void ABannedTermIsRefused()
    {
        // Built at run time so this file itself passes the copy guard.
        var result = AnswerCheck.Check("{{share.confirmed}} is primary " + "data.", Pack);

        Assert.Equal("a banned term", result.Problem);
    }

    [Fact]
    public void ALongAnswerIsRefused()
    {
        var result = AnswerCheck.Check("{{S1.name}} " + new string('x', AnswerCheck.MaxChars), Pack);

        Assert.Equal("answer too long", result.Problem);
    }

    [Fact]
    public void TheUnitsDigitIsNotANumber()
    {
        Assert.True(AnswerCheck.Check("{{total.carbon}} of CO2 equivalent, written CO₂e or tCO2e.", Pack).Ok);
    }

    [Fact]
    public void ThePackListsTheNamesAnAnswerMayOnlyCite()
    {
        Assert.Contains(Seed.Buyer.Name, Pack.Names);
        Assert.All(State.Nodes, n => Assert.Contains(n.Label, Pack.Names));
    }
}
