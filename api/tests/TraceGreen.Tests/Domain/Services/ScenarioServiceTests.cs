using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;
using TraceGreen.Infrastructure.Memory;

namespace TraceGreen.Tests.Domain.Services;

public class ScenarioServiceTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly Ctx Ctx = new(Ids.DemoWorkspace, "buyer");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid N3 = Guid.Parse("11111111-1111-4111-8111-000000000003");
    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed record Harness(ScenarioService Service, MemoryRepository Repo, InMemoryEventBus Bus);

    /// <summary>The seed start state plus N1 as verified by air (<see cref="TestNodes.N1"/>), the node the demo simulates.</summary>
    private static async Task<Harness> MakeAsync(decimal etsPrice = 70m)
    {
        var repo = new MemoryRepository();
        await repo.SeedAsync(Seed, reset: true, Ct);
        var invoice = new Invoice(TestNodes.N1InvoiceId, Ids.DemoWorkspace, "Shenzhen Diecast Co.", "CN1", "RO99999992", new DateOnly(2026, 10, 1),
            "EUR", 48000m, "raw-n1", "inv-n1",
            new ExtractedInvoice("N1-1", new DateOnly(2026, 10, 1), "EUR", 48000m, new Party("Shenzhen Diecast Co.", "CN1", "CN"),
                new Party("Buyer", "RO99999992", "RO"), [new ExtractedLine("1", "Zinc die-cast housings", 2300m, "KGM", 48000m, "CN", null, null)]),
            Now);
        await repo.InsertInvoiceWithNodeAsync(
            Ctx, invoice, TestNodes.N1(), prev => AuditChain.Build(prev, Ctx.WorkspaceId, Now, "test", "invoice.imported", new JsonObject()), Ct);

        var clock = new FakeTimeProvider(Now);
        var bus = new InMemoryEventBus(clock);
        var options = new ServiceOptions(TimeSpan.FromSeconds(10), TimeSpan.FromDays(7), "ip-hash:test", etsPrice);
        return new Harness(new ScenarioService(repo, bus, Seed, new NodeViewMapper(Seed), clock, options), repo, bus);
    }

    private static ScenarioChange Mode(TransportMode mode) => new(mode, null);

    private static ScenarioChange Swap(string alternativeId) => new(null, alternativeId);

    [Fact]
    public async Task N1AirToSeaIsWinWin()
    {
        var h = await MakeAsync();

        var result = await h.Service.SimulateAsync(Ctx, TestNodes.N1Id, Mode(TransportMode.Sea), Ct);

        Assert.True(result.IsOk);
        Assert.Equal(7488.8m, result.Value.CarbonKg);
        Assert.Equal(48690m, result.Value.CostEur);
        Assert.Equal(40, result.Value.LeadDays);
        Assert.Equal(MacBadge.WinWin, result.Value.Badge);
        Assert.True(result.Value.Illustrative);
    }

    [Fact]
    public async Task N1ToN1AltIsPremiumOrBelowEts()
    {
        var h = await MakeAsync();

        var result = await h.Service.SimulateAsync(Ctx, TestNodes.N1Id, Swap("N1-alt"), Ct);

        Assert.True(result.IsOk);
        Assert.Equal(6941.4m, result.Value.CarbonKg);
        Assert.Equal(2, result.Value.LeadDays);
        Assert.Contains(result.Value.Badge, new[] { MacBadge.Premium, MacBadge.BelowEts });
    }

    [Fact]
    public async Task TheBadgeUsesTheConfiguredCarbonPrice()
    {
        var cheap = await (await MakeAsync(etsPrice: 0m)).Service.SimulateAsync(Ctx, TestNodes.N1Id, Swap("N1-alt"), Ct);
        var dear = await (await MakeAsync(etsPrice: 1_000_000m)).Service.SimulateAsync(Ctx, TestNodes.N1Id, Swap("N1-alt"), Ct);

        Assert.Equal(MacBadge.Premium, cheap.Value.Badge);
        Assert.Equal(MacBadge.BelowEts, dear.Value.Badge);
    }

    [Fact]
    public async Task AlternativesAreThoseOfTheNodesCategory()
    {
        var h = await MakeAsync();

        var forN1 = await h.Service.AlternativesAsync(Ctx, TestNodes.N1Id, Ct);
        var forN3 = await h.Service.AlternativesAsync(Ctx, N3, Ct);

        Assert.Equal([new AlternativeView("N1-alt", "Cluj Metal Works SRL", "RO")], forN1.Value);
        Assert.Empty(forN3.Value);
    }

    [Theory]
    [InlineData(TransportMode.Road, null)]
    [InlineData(null, null)]
    [InlineData(TransportMode.Sea, "N1-alt")]
    [InlineData(null, "N2-alt")]
    public async Task AModeTheLaneLacksAnAlternativeOfAnotherCategoryOrNotExactlyOneChangeIsInvalid(TransportMode? mode, string? alternativeId)
    {
        var h = await MakeAsync();

        var result = await h.Service.SimulateAsync(Ctx, TestNodes.N1Id, new ScenarioChange(mode, alternativeId), Ct);

        Assert.Equal(ScenarioErrorCode.InvalidChange, result.Error.Code);
    }

    [Fact]
    public async Task AnUnknownAlternativeIsNotFound()
    {
        var h = await MakeAsync();

        var result = await h.Service.SimulateAsync(Ctx, TestNodes.N1Id, Swap("N9-alt"), Ct);

        Assert.Equal(ScenarioErrorCode.AlternativeNotFound, result.Error.Code);
    }

    [Fact]
    public async Task AnUnknownNodeIsNotFoundOnEveryRoute()
    {
        var h = await MakeAsync();
        var unknown = Ids.NewId();

        Assert.Equal(ScenarioErrorCode.NodeNotFound, (await h.Service.AlternativesAsync(Ctx, unknown, Ct)).Error.Code);
        Assert.Equal(ScenarioErrorCode.NodeNotFound, (await h.Service.SimulateAsync(Ctx, unknown, Mode(TransportMode.Sea), Ct)).Error.Code);
        Assert.Equal(ScenarioErrorCode.NodeNotFound, (await h.Service.SaveAsync(Ctx, unknown, Mode(TransportMode.Sea), Ct)).Error.Code);
    }

    [Fact]
    public async Task SimulatingStoresAndPublishesNothing()
    {
        var h = await MakeAsync();

        await h.Service.SimulateAsync(Ctx, TestNodes.N1Id, Mode(TransportMode.Sea), Ct);

        Assert.Empty(await h.Repo.ListScenariosAsync(Ctx, null, Ct));
        Assert.Empty(h.Bus.ReplaySince(Ctx.WorkspaceId, 0).Events);
    }

    [Fact]
    public async Task SavingStoresTheScenarioPublishesItAndLeavesTheNodeAlone()
    {
        var h = await MakeAsync();
        var before = await h.Repo.GetNodeAsync(Ctx, TestNodes.N1Id, Ct);

        var saved = await h.Service.SaveAsync(Ctx, TestNodes.N1Id, Mode(TransportMode.Sea), Ct);

        Assert.True(saved.IsOk);
        var scenario = saved.Value;
        Assert.Equal(TestNodes.N1Id, scenario.NodeId);
        Assert.Equal(ScenarioKind.Mode, scenario.Kind);
        Assert.Equal(Mode(TransportMode.Sea), scenario.Change);
        Assert.Equal(MacBadge.WinWin, scenario.Result.Badge);
        Assert.Equal(Now, scenario.CreatedAt);
        Assert.Equal([scenario.Id], (await h.Repo.ListScenariosAsync(Ctx, TestNodes.N1Id, Ct)).Select(s => s.Id));
        var published = Assert.IsType<ScenarioSaved>(Assert.Single(h.Bus.ReplaySince(Ctx.WorkspaceId, 0).Events));
        Assert.Equal(scenario.Id, published.Scenario.Id);
        Assert.Equal(before, await h.Repo.GetNodeAsync(Ctx, TestNodes.N1Id, Ct));
    }

    [Fact]
    public async Task ASavedSwapIsASupplierScenario()
    {
        var h = await MakeAsync();

        var saved = await h.Service.SaveAsync(Ctx, TestNodes.N1Id, Swap("N1-alt"), Ct);

        Assert.Equal(ScenarioKind.Supplier, saved.Value.Kind);
    }

    [Fact]
    public async Task AFailedSaveStoresAndPublishesNothing()
    {
        var h = await MakeAsync();

        var saved = await h.Service.SaveAsync(Ctx, TestNodes.N1Id, Mode(TransportMode.Road), Ct);

        Assert.False(saved.IsOk);
        Assert.Empty(await h.Repo.ListScenariosAsync(Ctx, null, Ct));
        Assert.Empty(h.Bus.ReplaySince(Ctx.WorkspaceId, 0).Events);
    }
}
