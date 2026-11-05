using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>
/// The simulator over HTTP (slice 5): the demo badges, the base node left alone, saving a scenario, and every error.
/// Each test starts from the seed state through the reset route, since the class shares one host.
/// </summary>
public class ScenarioEndpointTests : IClassFixture<TestWebAppFactory>
{
    private static readonly Guid N1 = TestNodes.N1Id;
    private static readonly Guid N4 = Guid.Parse("11111111-1111-4111-8111-000000000004");
    private static readonly string N1Xml = File.ReadAllText(Path.Combine(TestPaths.SeedDir, "invoices", "N1-shenzhen.xml"));

    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;

    public ScenarioEndpointTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IRepository Repo => _factory.Services.GetRequiredService<IRepository>();

    private IEventBus Bus => _factory.Services.GetRequiredService<IEventBus>();

    private static Ctx Ctx => new(Ids.DemoWorkspace, "test");

    private async Task Reset() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/dev/reset", null, Ct)).StatusCode);

    private async Task ImportN1() =>
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/ingest/demo", null, Ct)).StatusCode);

    /// <summary>The demo's state when the simulator is shown: N1 imported, then confirmed by air on grid power.</summary>
    private async Task ImportAndVerifyN1ByAir()
    {
        await ImportN1();
        var answer = await _client.PostAsJsonAsync("/api/dev/simulate-answer", new { nodeId = N1, transportMode = "air", energySource = "grid" }, Ct);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
    }

    private Task<HttpResponseMessage> Simulate(object body) => _client.PostAsJsonAsync("/api/simulate", body, Ct);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task VerifiedN1AirToSeaIsWinWin()
    {
        await Reset();
        await ImportAndVerifyN1ByAir();

        var result = await Ok(await Simulate(new { nodeId = N1, mode = "sea" }));

        // Same figures as SimulatorTests.N1AirToSeaIsWinWin: the service only resolves the inputs.
        Assert.Equal("win-win", result.GetProperty("badge").GetString());
        Assert.Equal(7488.8m, result.GetProperty("carbonKg").GetDecimal());
        Assert.Equal(48690m, result.GetProperty("costEur").GetDecimal());
        Assert.Equal(40, result.GetProperty("leadDays").GetInt32());
        Assert.Equal(33, result.GetProperty("delta").GetProperty("leadDays").GetInt32());
        Assert.Equal("abatement", result.GetProperty("mac").GetProperty("kind").GetString());
        Assert.True(result.GetProperty("illustrative").GetBoolean());
        Assert.False(result.GetProperty("massUnknown").GetBoolean());
        Assert.Contains("PLACEHOLDER", result.GetProperty("inputs")[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task VerifiedN1ToN1AltIsPremiumOrBelowEts()
    {
        await Reset();
        await ImportAndVerifyN1ByAir();

        var result = await Ok(await Simulate(new { nodeId = N1, alternativeId = "N1-alt" }));

        Assert.Contains(result.GetProperty("badge").GetString(), new[] { "premium", "below-ets" });
        Assert.Equal(6941.4m, result.GetProperty("carbonKg").GetDecimal());
        Assert.Equal(2, result.GetProperty("leadDays").GetInt32());
    }

    [Fact]
    public async Task EstimatedN1KeepsTheDemoBadgesAgainstItsSpendEstimate()
    {
        await Reset();
        await ImportN1();

        var sea = await Ok(await Simulate(new { nodeId = N1, mode = "sea" }));
        var swap = await Ok(await Simulate(new { nodeId = N1, alternativeId = "N1-alt" }));

        Assert.Equal("win-win", sea.GetProperty("badge").GetString());
        Assert.Contains(swap.GetProperty("badge").GetString(), new[] { "premium", "below-ets" });
    }

    [Fact]
    public async Task SimulatingLeavesTheNodeAndTheEventStreamAlone()
    {
        await Reset();
        await ImportAndVerifyN1ByAir();
        var before = await Repo.GetNodeAsync(Ctx, N1, Ct);
        var seq = Bus.LastSeq(Ids.DemoWorkspace);

        await Ok(await Simulate(new { nodeId = N1, mode = "sea" }));
        await Ok(await Simulate(new { nodeId = N1, alternativeId = "N1-alt" }));

        Assert.Equal(before, await Repo.GetNodeAsync(Ctx, N1, Ct));
        Assert.Equal(seq, Bus.LastSeq(Ids.DemoWorkspace));
        Assert.Empty(await Repo.ListScenariosAsync(Ctx, N1, Ct));
    }

    [Fact]
    public async Task SavingRecomputesStoresAndPublishesTheScenario()
    {
        await Reset();
        await ImportAndVerifyN1ByAir();
        var before = await Repo.GetNodeAsync(Ctx, N1, Ct);
        var seq = Bus.LastSeq(Ids.DemoWorkspace);

        var response = await _client.PostAsJsonAsync("/api/scenarios", new { nodeId = N1, mode = "sea" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(N1, body.GetProperty("nodeId").GetGuid());
        Assert.Equal("mode", body.GetProperty("kind").GetString());
        Assert.Equal("sea", body.GetProperty("change").GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("change").GetProperty("alternativeId").ValueKind);
        Assert.Equal("win-win", body.GetProperty("result").GetProperty("badge").GetString());

        var stored = Assert.Single(await Repo.ListScenariosAsync(Ctx, N1, Ct));
        Assert.Equal(body.GetProperty("id").GetGuid(), stored.Id);
        var saved = Assert.Single(Bus.ReplaySince(Ids.DemoWorkspace, seq).Events.OfType<ScenarioSaved>());
        Assert.Equal(stored.Id, saved.Scenario.Id);
        Assert.Equal(before, await Repo.GetNodeAsync(Ctx, N1, Ct));
    }

    [Fact]
    public async Task ASavedSupplierSwapIsNormalisedToItsAlternative()
    {
        await Reset();
        await ImportN1();

        var response = await _client.PostAsJsonAsync("/api/scenarios", new { nodeId = N1, mode = (string?)null, alternativeId = "N1-alt" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("supplier", body.GetProperty("kind").GetString());
        Assert.Equal("N1-alt", body.GetProperty("change").GetProperty("alternativeId").GetString());
    }

    [Fact]
    public async Task NodesListTheAlternativesOfTheirCategory()
    {
        await Reset();
        await ImportN1();

        var nodes = (await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct)).GetProperty("nodes").EnumerateArray().ToList();

        var n1 = nodes.Single(n => n.GetProperty("id").GetGuid() == N1);
        var alternative = Assert.Single(n1.GetProperty("alternatives").EnumerateArray());
        Assert.Equal("N1-alt", alternative.GetProperty("id").GetString());
        Assert.Equal("Cluj Metal Works SRL", alternative.GetProperty("label").GetString());
        Assert.Equal("RO", alternative.GetProperty("country").GetString());
        Assert.Equal("Cluj-Napoca, RO", alternative.GetProperty("origin").GetString());
        Assert.Equal("road", alternative.GetProperty("mode").GetString());
        Assert.True(alternative.GetProperty("illustrative").GetBoolean());
        Assert.Empty(nodes.Single(n => n.GetProperty("id").GetGuid() == N4).GetProperty("alternatives").EnumerateArray());
    }

    [Fact]
    public async Task UnknownNodeOrAlternativeIs404()
    {
        await Reset();
        await ImportN1();

        await AssertProblem(await Simulate(new { nodeId = Guid.NewGuid(), mode = "sea" }), HttpStatusCode.NotFound, "NodeNotFound");
        await AssertProblem(await Simulate(new { nodeId = N1, alternativeId = "N9-alt" }), HttpStatusCode.NotFound, "AlternativeNotFound");
        await AssertProblem(
            await _client.PostAsJsonAsync("/api/scenarios", new { nodeId = Guid.NewGuid(), mode = "sea" }, Ct), HttpStatusCode.NotFound, "NodeNotFound");
    }

    [Fact]
    public async Task NeitherOrBothChangesOrAnotherCategorysAlternativeIs422()
    {
        await Reset();
        await ImportN1();

        await AssertProblem(await Simulate(new { nodeId = N1 }), HttpStatusCode.UnprocessableEntity, "InvalidChange");
        await AssertProblem(await Simulate(new { nodeId = N1, mode = "sea", alternativeId = "N1-alt" }), HttpStatusCode.UnprocessableEntity, "InvalidChange");
        // N2-alt makes printed circuit boards; N1 buys zinc die-cast parts.
        await AssertProblem(await Simulate(new { nodeId = N1, alternativeId = "N2-alt" }), HttpStatusCode.UnprocessableEntity, "InvalidChange");
    }

    [Fact]
    public async Task AModeTheLaneLacksIs422AndNothingIsSaved()
    {
        await Reset();
        await ImportN1();
        var seq = Bus.LastSeq(Ids.DemoWorkspace);

        await AssertProblem(await Simulate(new { nodeId = N1, mode = "road" }), HttpStatusCode.UnprocessableEntity, "ModeNotAvailable");
        await AssertProblem(
            await _client.PostAsJsonAsync("/api/scenarios", new { nodeId = N1, mode = "road" }, Ct), HttpStatusCode.UnprocessableEntity, "ModeNotAvailable");

        Assert.Empty(await Repo.ListScenariosAsync(Ctx, N1, Ct));
        Assert.Equal(seq, Bus.LastSeq(Ids.DemoWorkspace));
    }

    [Fact]
    public async Task AModeChangeWithoutAMassIs422ButASwapStillWorks()
    {
        await Reset();
        // Counted in pieces, so the invoice gives no mass.
        var xml = N1Xml
            .Replace("<cbc:InvoicedQuantity unitCode=\"KGM\">2300</cbc:InvoicedQuantity>", "<cbc:InvoicedQuantity unitCode=\"H87\">2300</cbc:InvoicedQuantity>")
            .Replace("SDC-2026-1028", "SDC-2026-PCS");
        var imported = await _client.PostAsync("/api/ingest/ubl", new StringContent(xml, Encoding.UTF8, "text/xml"), Ct);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        var node = (await Json(imported)).GetProperty("node");
        Assert.Equal(JsonValueKind.Null, node.GetProperty("massT").ValueKind);
        var nodeId = node.GetProperty("id").GetGuid();

        await AssertProblem(await Simulate(new { nodeId, mode = "sea" }), HttpStatusCode.UnprocessableEntity, "MassUnknown");
        await Ok(await Simulate(new { nodeId, alternativeId = "N1-alt" }));
    }
}
