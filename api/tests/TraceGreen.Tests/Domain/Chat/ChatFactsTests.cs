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

    [Theory]
    [InlineData("Still estimated: {{list.estimated}}, that is {{S2.name}} and {{S3.name}}.")]
    public void TwoFactsOfTheSameValueAreAValueRepeatedOnlyWhenTheValuesMatch(string text) =>
        Assert.True(AnswerCheck.Check(text, Pack).Ok, AnswerCheck.Check(text, Pack).Problem);

    [Fact]
    public void TheSameValueByTwoFactsIsRepeated()
    {
        var oneEstimated = Pack.Facts.Single(f => f.Id == "list.estimated").Value.Split(", ");
        Assert.Equal(2, oneEstimated.Length); // the seed: two estimated suppliers, so the list is not one name
        Assert.Equal("a value repeated", AnswerCheck.Check("{{S2.status}} and {{S3.status}} both.", Pack).Problem);
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
    // The system prompt's own general examples (ai/app/chat.py) and how TraceGreen works, which must stay general.
    [InlineData("Hello! I can tell you who emits the most, what is still estimated and what a change of transport or supplier would do. What would you like to know?")]
    [InlineData("I can tell you who emits the most, which suppliers are still estimated, what the total is and what a change of transport or supplier would do. Ask about any supplier, or tell me what you are trying to decide.")]
    [InlineData("I can't change anything myself, I only read your data. Open the supplier on the Suppliers screen and use What if to try the change, then Apply to save it.")]
    [InlineData("I can only help with your suppliers and emissions. Try asking which supplier emits the most.")]
    [InlineData("A supplier confirms a delivery in two taps, through a single-use link the buyer sends with Request data on the Suppliers screen.")]
    [InlineData("The figure then follows the confirmed transport mode, and the delivery becomes supplier-confirmed, or partly confirmed when one answer is skipped.")]
    [InlineData("Each delivery starts as a spend-based estimate until its supplier confirms it.")]
    [InlineData("The supplier-confirmed share is measured by emissions, not by node count.")]
    [InlineData("Purchased goods are usually the largest source of a factory's emissions, and air freight emits far more than sea.")]
    public void AGeneralAnswerMayCiteNoFact(string text)
    {
        var result = AnswerCheck.Check(text, Pack);

        Assert.True(result.Ok, result.Problem);
        Assert.Empty(result.FactIds);
        Assert.Equal(text, Assert.Single(result.Segments).Text);
    }

    // Review of #54 (2026-11-07): a sentence that cites no fact passed as general while it claimed something about the
    // data, some of it false on the seed (S1 is supplier-confirmed; two of four deliveries are estimated).
    [Theory]
    [InlineData("The biggest emitter ships by air.")]
    [InlineData("The board maker is the largest emitter.")]
    [InlineData("Your Taiwanese supplier emits the most.")]
    [InlineData("None of the suppliers has confirmed a delivery yet.")]
    [InlineData("Every delivery is supplier-confirmed.")]
    [InlineData("Almost every delivery is still estimated.")]
    [InlineData("Two deliveries are still estimated.")]
    [InlineData("The suppliers are mostly estimated.")]
    [InlineData("Most of the emissions come from one supplier.")]
    [InlineData("Transport is the main source of your emissions.")]
    [InlineData("The total is large.")]
    [InlineData("Your footprint is dominated by air freight.")]
    [InlineData("You have four suppliers.")]
    [InlineData("There is no what-if for that supplier.")]
    [InlineData("I can tell you that the biggest emitter ships by air.")]
    public void AnUncitedClaimAboutTheDataIsRefused(string text) =>
        Assert.Equal("a claim about the data without a fact", AnswerCheck.Check(text, Pack).Problem);

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
    // A comparison among the suppliers just named, not all of them (2026-11-07).
    [InlineData("{{S2.name}} emits {{S2.carbon}} and {{S4.name}} emits {{S4.carbon}}, so the larger of the two is the one to ask first.")]
    [InlineData("{{S2.name}} has {{S2.carbon}} and {{S4.name}} has {{S4.carbon}}. Of the two, {{S2.name}} has the larger figure.")]
    [InlineData("{{S3.name}} and {{S4.name}} are left. The largest of the two is {{S3.name}}.")]
    [InlineData("{{S3.name}} and {{S4.name}} are left. The smaller of the two is {{S4.name}}.")]
    [InlineData("Ask {{S3.name}} first, since it emits {{S3.carbon}}, the most of the two.")]
    [InlineData("By emissions, {{S3.name}} is the larger of the two at {{S3.carbon}}, compared with {{S4.carbon}} for {{S4.name}}.")]
    [InlineData("{{S3.name}} and {{S4.name}} are left. Ask {{S3.name}} first, since it is the larger of the two.")]
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
    [InlineData("{{S2.name}} has {{S2.carbon}} and {{S4.name}} has {{S4.carbon}}. Of the two, {{S4.name}} has the larger figure.", "a comparison the data does not hold")]
    [InlineData("{{S3.name}} and {{S4.name}} are left. The smaller of the two is {{S3.name}}.", "a comparison the data does not hold")]
    // Claude Sonnet 5.5 (2026-11-07): the smaller of two named suppliers called the larger, with both cited.
    [InlineData("By emissions, {{S4.name}} is the larger of the two at {{S4.carbon}}, compared with {{S3.carbon}} for {{S3.name}}.", "a comparison the data does not hold")]
    [InlineData("{{S3.name}} and {{S4.name}} are left. Ask {{S4.name}} first, since it is the larger of the two.", "a comparison the data does not hold")]
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

/// <summary>The what-if facts and the checks the free chat needed after a real conversation (2026-11-07).</summary>
public class ChatWhatIfTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly DemoStateData State = DemoState.Build(Seed);
    private static readonly NodeViewMapper Mapper = new(Seed);

    private static readonly IReadOnlyList<NodeView> Views =
        State.Nodes.Select(n => Mapper.Map(n, State.Invoices.Single(i => i.Id == n.InvoiceId))).OrderByDescending(n => n.CarbonKg).ToList();

    private static ScenarioResult Result(decimal carbonKg, decimal costEur, int days, MacBadge badge) =>
        new(0, 0, 0, new ScenarioDelta(carbonKg, costEur, days),
            new MacResult(carbonKg < 0 ? MacKind.Abatement : MacKind.NotAbatement, null, costEur, carbonKg / 1000m), badge, [], false, true);

    // S1 to air costs more and emits more; S1 to sea saves both; S2 to rail saves a little carbon at a premium.
    private static readonly FactPack Pack = FactPack.Build(
        new GraphSnapshot(Seed.Buyer, Views, KpiCalculator.Compute(State.Nodes), [], 0, "memory"),
        [],
        [
            new WhatIf(Views[0], new ScenarioChange(TransportMode.Air, null), Result(3800m, 1620m, -20, MacBadge.NotAbatement)),
            new WhatIf(Views[0], new ScenarioChange(TransportMode.Sea, null), Result(-14000m, -3910m, 33, MacBadge.WinWin)),
            new WhatIf(Views[1], new ScenarioChange(TransportMode.Rail, null), Result(-400m, 900m, 2, MacBadge.Premium)),
        ]);

    private static Fact F(string id) => Pack.Find(id) ?? throw new InvalidOperationException($"no fact {id}");

    [Fact]
    public void WhatIfsAreNumberedByTheirCutInEmissionsWithSignedValuesForTheModel()
    {
        Assert.Equal("switch to sea", F("W1.change").Value);
        Assert.Equal("-14000", F("W1.carbonDelta").Value);
        Assert.Equal("-14.00 t CO2e (less)", F("W1.carbonDelta").PromptText);
        Assert.Equal("-EUR 3,910 (less)", F("W1.costDelta").PromptText);
        Assert.Equal("+33 days (slower)", F("W1.leadDelta").PromptText);
        Assert.Equal("win-win", F("W1.badge").Value);
        Assert.Contains("the largest cut in emissions", F("W1.carbonDelta").About);
        Assert.Equal("switch to air", F("W3.change").Value);
        Assert.Equal("+3.80 t CO2e (more)", F("W3.carbonDelta").PromptText);
        Assert.Equal(Views[0].Id, F("W3.supplier").NodeId);
        Assert.Equal("3", F("count.whatIfs").Value);
    }

    [Theory]
    [InlineData("Switching {{W1.supplier}} to sea cuts emissions by {{W1.carbonDelta}} and cost by {{W1.costDelta}}.")]
    [InlineData("Switching to air would raise emissions by {{W3.carbonDelta}} and add {{W3.costDelta}} to the cost.")]
    [InlineData("Sea cuts {{W1.carbonDelta}} but adds {{W1.leadDelta}} to the lead time.")]
    [InlineData("For {{W1.supplier}}: {{W1.change}}, {{W1.carbonDelta}}, {{W1.costDelta}}.")]
    // Claude Sonnet 5.5's answers to "How can I lower my emissions?" (2026-11-07), refused before a direction word held
    // only for its own quantity: the lead time that rises was read against "cuts" and "lowers".
    [InlineData("The what-if that cuts emissions the most is for {{W1.supplier}}: {{W1.change}} would change emissions by {{W1.carbonDelta}} and cost by {{W1.costDelta}}, though lead time changes by {{W1.leadDelta}}.")]
    [InlineData("For {{W1.supplier}}, {{W1.change}} lowers emissions ({{W1.carbonDelta}}) and lowers cost ({{W1.costDelta}}), with lead time {{W1.leadDelta}}.")]
    [InlineData("{{W1.change}} would cut cost by {{W1.costDelta}} and emissions by {{W1.carbonDelta}}.")]
    [InlineData("Rail cuts emissions by {{W2.carbonDelta}} but costs {{W2.costDelta}} more.")]
    [InlineData("Cost would rise by {{W2.costDelta}}, while emissions fall by {{W2.carbonDelta}}.")]
    public void ChangesTheDataHoldsPass(string text) =>
        Assert.True(AnswerCheck.Check(text, Pack).Ok, AnswerCheck.Check(text, Pack).Problem);

    [Theory]
    [InlineData("Switching {{W3.supplier}} to air would cut {{S1.carbon}} by {{W3.carbonDelta}}.", "a change the data does not hold")]
    [InlineData("Switching to air saves {{W3.costDelta}} and reduces {{W3.carbonDelta}}.", "a change the data does not hold")]
    [InlineData("Rail lowers the cost by {{W2.costDelta}}.", "a change the data does not hold")]
    [InlineData("Going by sea raises emissions by {{W1.carbonDelta}}.", "a change the data does not hold")]
    [InlineData("{{W1.change}} cuts emissions by {{W1.carbonDelta}}, cost by {{W1.costDelta}} and lead time by {{W1.leadDelta}}.", "a change the data does not hold")]
    [InlineData("Rail lowers emissions by {{W2.carbonDelta}} and cost by {{W2.costDelta}}.", "a change the data does not hold")]
    [InlineData("I'll add a what-if for {{S2.name}}.", "an action the assistant cannot take")]
    [InlineData("I have changed the transport mode of {{S1.name}}.", "an action the assistant cannot take")]
    [InlineData("Let me send a request to {{S1.name}}.", "an action the assistant cannot take")]
    [InlineData("The largest are {{S1.name}} ({{S1.carbon}}) and {{S2.name}} ({{S1.carbon}}).", "a value repeated")]
    [InlineData("Supplier-confirmed: {{S1.status}}, like {{S1.status}}.", "a value repeated")]
    public void ChangesActionsAndRepeatsTheDataDoesNotHoldAreRefused(string text, string problem) =>
        Assert.Equal(problem, AnswerCheck.Check(text, Pack).Problem);

    [Fact]
    public void AWhatIfStaysWithItsOwnSupplier()
    {
        Assert.Equal("a what-if given to another supplier", AnswerCheck.Check("Switching {{S1.name}} to rail cuts {{W2.carbonDelta}}.", Pack).Problem);
        Assert.True(AnswerCheck.Check("By rail, {{W2.supplier}} cuts {{W2.carbonDelta}}.", Pack).Ok);
        Assert.True(AnswerCheck.Check("By rail, {{S2.name}} cuts {{W2.carbonDelta}}.", Pack).Ok);
        Assert.True(AnswerCheck.Check("{{W1.supplier}} by sea cuts {{W1.carbonDelta}}, and {{W2.supplier}} by rail cuts {{W2.carbonDelta}}.", Pack).Ok);
        // "Its" is the supplier the sentence before named.
        Assert.Equal("a what-if given to another supplier",
            AnswerCheck.Check("Open {{S1.name}} in What if. Switching its transport to rail would cut {{W2.carbonDelta}}.", Pack).Problem);
        Assert.True(AnswerCheck.Check("{{W2.supplier}} could go by rail. It would cut {{W2.carbonDelta}}.", Pack).Ok);
        // A figure of one supplier beside another's what-if.
        Assert.Equal("a what-if given to another supplier",
            AnswerCheck.Check("To lower {{S1.carbon}}, try {{W2.change}} on {{W2.supplier}}.", Pack).Problem);
        Assert.Equal("a change the supplier has no what-if for",
            AnswerCheck.Check("Open {{S3.name}} on the Suppliers screen. Try its road transport to rail.", Pack).Problem);
    }

    [Fact]
    public void ASupplierIsSentOnlyToAModeItsWhatIfsOffer()
    {
        // S1 has sea and air what-ifs, S2 rail, S3 none at all.
        Assert.Equal("a change the supplier has no what-if for", AnswerCheck.Check("Open {{S3.name}} and try switching to rail in What if.", Pack).Problem);
        Assert.Equal("a change the supplier has no what-if for", AnswerCheck.Check("Try {{S1.name}} to rail instead.", Pack).Problem);
        Assert.True(AnswerCheck.Check("Try {{S1.name}} to sea in What if.", Pack).Ok);
        Assert.True(AnswerCheck.Check("Switching {{S2.name}} to rail cuts {{W2.carbonDelta}}.", Pack).Ok);
        Assert.True(AnswerCheck.Check("{{S3.name}} has no what-if: {{S3.whatIf}}.", Pack).Ok);
    }

    [Fact]
    public void ALargeSupplierWithNoWhatIfSaysWhy()
    {
        Assert.Null(Pack.Find("S1.whatIf"));
        Assert.Null(Pack.Find("S2.whatIf"));
        Assert.Equal("none", F("S3.whatIf").Value);
        Assert.StartsWith("none: ", F("S3.whatIf").PromptText);
    }

    [Fact]
    public void WhatTheAssistantCanOfferIsNotAPromise()
    {
        Assert.True(AnswerCheck.Check("I can't change suppliers, but in What if you can compare {{W1.change}} for {{W1.supplier}}.", Pack).Ok);
        Assert.True(AnswerCheck.Check("I can show you what a change of transport would do.", Pack).Ok);
    }

    [Fact]
    public void MarkdownIsShownAsPlainText()
    {
        var result = AnswerCheck.Check("Compare transport modes in **What if** or *Request data*.\n- `Sea` is one.", Pack);

        Assert.True(result.Ok, result.Problem);
        Assert.Equal("Compare transport modes in What if or Request data.\nSea is one.", Assert.Single(result.Segments).Text);
    }
}

public class ChatRepeatSupplierTests
{
    [Fact]
    public void ASecondDeliveryFromTheSameSupplierSaysSo()
    {
        var seed = SeedLoader.Load(TestPaths.SeedDir);
        var state = DemoState.Build(seed);
        var mapper = new NodeViewMapper(seed);
        var views = state.Nodes.Select(n => mapper.Map(n, state.Invoices.Single(i => i.Id == n.InvoiceId))).OrderByDescending(n => n.CarbonKg).ToList();
        var twice = views.Append(views[1] with { Id = Guid.NewGuid(), CarbonKg = views[1].CarbonKg - 1m }).ToList();

        var pack = FactPack.Build(new GraphSnapshot(seed.Buyer, twice, KpiCalculator.Compute(state.Nodes), [], 0, "memory"), []);

        Assert.DoesNotContain("another delivery", pack.Find("S2.name")!.About);
        Assert.Contains("another delivery from the same supplier as S2", pack.Find("S3.name")!.About);
    }
}
