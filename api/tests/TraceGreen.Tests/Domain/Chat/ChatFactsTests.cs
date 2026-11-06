using System.Globalization;
using System.Text.Json.Nodes;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Chat;
using TraceGreen.Domain.Copy;
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
    [InlineData("Most of it is estimated.", "a claim about the data without a fact")]
    [InlineData("Your largest supplier is the board maker.", "a claim about the data without a fact")]
    [InlineData("Your suppliers are still estimated. Ask {{S2.name}} first.", "a claim about the data without a fact")]
    [InlineData("ISO 14064 covers this.", "a number written out")]
    [InlineData("The largest is {{S1.name}}. FactIds: {{S1.name}}.", "the answer's format in its text")]
    [InlineData("Kind: general. Hello.", "the answer's format in its text")]
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

    [Theory]
    [InlineData("I can tell you who emits the most, which suppliers are still estimated and what a change of transport would do.")]
    [InlineData("Estimated means the figure comes from the invoice spend; supplier-confirmed means the supplier answered.")]
    [InlineData("Most emissions of a factory come from what it buys, its Scope 3. Start with the largest emitters.")]
    public void AGeneralAnswerMayCiteNoFact(string text)
    {
        var result = AnswerCheck.Check(text, Pack);

        Assert.True(result.Ok, result.Problem);
        Assert.Empty(result.FactIds);
        Assert.Equal(text, Assert.Single(result.Segments).Text);
    }

    [Fact]
    public void ThePackListsTheNamesAnAnswerMayOnlyCite()
    {
        Assert.Contains(Seed.Buyer.Name, Pack.Names);
        Assert.All(State.Nodes, n => Assert.Contains(n.Label, Pack.Names));
    }

    // The seed by emissions since the sourced factors (#45): S1 Taiwan (supplier-confirmed), S2 Oradea (estimated), S3 Sibiu (estimated), S4 Hamburg (partly).
    [Fact]
    public void TheSeedRanksAsTheClaimTestsExpect()
    {
        Assert.Equal(["VERIFIED", "ESTIMATED", "ESTIMATED", "PARTIAL"], Enumerable.Range(1, 4).Select(i => F($"S{i}.status").Value));
        Assert.Equal(4, Pack.SupplierCount);
    }

    [Theory]
    [InlineData("{{S2.name}} and {{S3.name}} are still estimated.")]
    [InlineData("{{S1.name}} is supplier-confirmed, at {{S1.carbon}}.")]
    [InlineData("{{S4.name}} is partly confirmed.")]
    [InlineData("{{S2.name}} is not yet confirmed.")]
    [InlineData("The largest emitter is {{S1.name}}; the smallest is {{S4.name}}.")]
    [InlineData("{{S1.name}} emits more than {{S3.name}}.")]
    [InlineData("{{S3.name}} emits less than {{S1.name}}.")]
    [InlineData("Still estimated: {{list.estimated}}.")]
    [InlineData("{{S1.name}} is confirmed. {{S2.name}} is estimated.")]
    [InlineData("The second largest emitter is {{S2.name}}, with {{S2.carbon}}.")]
    [InlineData("The third-largest is {{S3.name}}.")]
    [InlineData("The largest emitter still estimated is {{S2.name}}; ask it to confirm.")]
    [InlineData("The second largest estimated emitter is {{S3.name}}.")]
    [InlineData("{{S1.name}} emits the most. The next largest is {{S2.name}}.")]
    [InlineData("The largest estimated is {{S2.name}}; the next largest estimated is {{S3.name}}.")]
    [InlineData("The second smallest is {{S3.name}}.")]
    [InlineData("The largest emitters are {{S1.name}} and {{S2.name}}.")]
    [InlineData("The largest estimated suppliers are {{S2.name}} and {{S3.name}}.")]
    public void ClaimsTheDataHoldsPass(string text) =>
        Assert.True(AnswerCheck.Check(text, Pack).Ok, AnswerCheck.Check(text, Pack).Problem);

    [Theory]
    [InlineData("The supplier still estimated is {{S1.name}}, with {{S1.carbon}}.", "a status claim the data does not hold")]
    [InlineData("{{S4.name}} is confirmed.", "a status claim the data does not hold")]
    [InlineData("{{S1.name}} is not confirmed yet.", "a status claim the data does not hold")]
    [InlineData("The largest emitter is {{S2.name}}.", "a largest claim the data does not hold")]
    [InlineData("The smallest emitter is {{S1.name}}.", "a smallest claim the data does not hold")]
    [InlineData("{{S3.name}} emits more than {{S1.name}}.", "a comparison the data does not hold")]
    [InlineData("{{S1.name}} emits less than {{S3.name}}.", "a comparison the data does not hold")]
    [InlineData("The second largest emitter is {{S3.name}}.", "a largest claim the data does not hold")]
    [InlineData("The largest estimated emitter is {{S3.name}}.", "a largest claim the data does not hold")]
    [InlineData("The fifth largest is {{S4.name}}.", "a largest claim the data does not hold")]
    [InlineData("The smallest estimated emitter is {{S2.name}}.", "a smallest claim the data does not hold")]
    [InlineData("The largest emitters are {{S1.name}} and {{S3.name}}.", "a largest claim the data does not hold")]
    [InlineData("The largest is {{S1.name}}. The next largest is {{S3.name}}.", "a largest claim the data does not hold")]
    [InlineData("The next largest is {{S3.name}}.", "a largest claim the data does not hold")]
    [InlineData("The largest is {{S1.name}}. The next largest is {{S3.name}}, which is estimated.", "a largest claim the data does not hold")]
    public void ClaimsTheDataDoesNotHoldAreRefused(string text, string problem) =>
        Assert.Equal(problem, AnswerCheck.Check(text, Pack).Problem);

    [Fact]
    public void ALongAnswerIsShownWithoutItsOneFalseSentence()
    {
        var trimmed = AnswerCheck.Trim(
            "The largest emitter is {{S1.name}}. Ask for the rest with Request data. The next largest is {{S3.name}}.", Pack);

        Assert.NotNull(trimmed);
        Assert.Equal(["S1.name"], trimmed.FactIds);
        Assert.Equal(". Ask for the rest with Request data.", trimmed.Segments[^1].Text);
    }

    [Theory]
    [InlineData("The largest emitter is {{S2.name}}. The next is {{S1.name}}.")]
    [InlineData("The largest emitter is {{S2.name}}. Ask it to confirm. Then compare modes in What if.")]
    [InlineData("The total is 19.4 t. It is 20 t next year. And 21 t after.")]
    [InlineData("The largest emitters are {{S1.name}} and {{S3.name}}. Confirm their deliveries first. Start with {{S1.name}}.")]
    public void AnAnswerIsNotTrimmedWhenTooShortWhenNothingCitedIsLeftOrWhenOneSentenceIsNotEnough(string text) =>
        Assert.Null(AnswerCheck.Trim(text, Pack));

    [Fact]
    public void EveryRuleHasARetryHintThatHoldsNoData()
    {
        string[] rules =
        [
            "a number written out", "the answer's format in its text", "a name written out", "a company name written out", "a banned term",
            "a status claim the data does not hold", "a largest claim the data does not hold",
            "a smallest claim the data does not hold", "a comparison the data does not hold",
            "a claim about the data without a fact",
        ];

        Assert.All(rules, rule => Assert.NotEqual(rule, AnswerCheck.RetryHint(rule)));
        Assert.All(rules, rule => Assert.False(BannedTerms.Any(AnswerCheck.RetryHint(rule))));
        Assert.Equal("unknown fact S9.name", AnswerCheck.RetryHint("unknown fact S9.name"));
    }

    [Fact]
    public void TheStatusListsAreTheApisOwn()
    {
        Assert.Contains("Oradea", F("list.estimated").Value);
        Assert.Contains("Sibiu", F("list.estimated").Value);
        Assert.DoesNotContain("Taiwan", F("list.estimated").Value);
        Assert.Contains("Taiwan", F("list.confirmed").Value);
    }
}
