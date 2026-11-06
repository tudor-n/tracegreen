using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Api;

/// <summary>
/// The classification review (F6) over HTTP: the review state on every node, confirming and correcting a category, the
/// recomputed figures, every error, the audit entry and event, and the supplier's link after a correction. Each test starts
/// from the seed state through the reset route, since the class shares one host.
/// </summary>
public class ClassificationEndpointTests : IClassFixture<VerifyWebAppFactory>
{
    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly Guid N3 = Guid.Parse("11111111-1111-4111-8111-000000000003");

    private readonly VerifyWebAppFactory _factory;
    private readonly HttpClient _client;

    public ClassificationEndpointTests(VerifyWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SeedBundle Seed => _factory.Services.GetRequiredService<SeedBundle>();

    private IRepository Repo => _factory.Services.GetRequiredService<IRepository>();

    private IEventBus Bus => _factory.Services.GetRequiredService<IEventBus>();

    private static Ctx Ctx => new(Ids.DemoWorkspace, "test");

    private static FactorTable Table(SeedBundle seed) => FactorTable.FromSeed(seed);

    private async Task Reset() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/dev/reset", null, Ct)).StatusCode);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<JsonElement> Node(Guid id)
    {
        var graph = await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct);
        return graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == id);
    }

    private Task<HttpResponseMessage> Review(Guid nodeId, string categoryId, string expectedCategoryId) =>
        _client.PostAsJsonAsync($"/api/nodes/{nodeId:D}/classification", new { categoryId, expectedCategoryId }, Ct);

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
    public async Task AnAiMappedNodeWaitsForReviewAndAnInvoiceCodedOneDoesNot()
    {
        await Reset();

        // N3's category comes from the AI cache, N2's from the CPV code on its invoice.
        var n3 = (await Node(N3)).GetProperty("classification");
        var n2 = (await Node(N2)).GetProperty("classification");

        Assert.True(n3.GetProperty("aiMapped").GetBoolean());
        Assert.Equal("pending", n3.GetProperty("review").GetString());
        Assert.Equal(JsonValueKind.Null, n3.GetProperty("reviewedAt").ValueKind);
        Assert.Equal("invoice", n2.GetProperty("source").GetString());
        Assert.Equal("notNeeded", n2.GetProperty("review").GetString());
        Assert.Equal(JsonValueKind.Null, n2.GetProperty("reviewedAt").ValueKind);
    }

    [Fact]
    public async Task ConfirmingKeepsTheCategoryAndItsFiguresAndHandsItToTheBuyer()
    {
        await Reset();
        var before = await Node(N3);

        var node = await Ok(await Review(N3, "wire_harness", "wire_harness"));

        var classification = node.GetProperty("classification");
        Assert.Equal("wire_harness", classification.GetProperty("categoryId").GetString());
        Assert.Equal("confirmed", classification.GetProperty("review").GetString());
        Assert.Equal("buyer", classification.GetProperty("source").GetString());
        Assert.False(classification.GetProperty("aiMapped").GetBoolean());
        Assert.Equal(JsonValueKind.Null, classification.GetProperty("confidence").ValueKind);
        Assert.Equal(_factory.Clock.GetUtcNow().ToUnixTimeMilliseconds(), classification.GetProperty("reviewedAt").GetDateTimeOffset().ToUnixTimeMilliseconds());
        Assert.Equal(before.GetProperty("carbonKg").GetDecimal(), node.GetProperty("carbonKg").GetDecimal());
        // The answer is the node the graph now shows.
        Assert.Equal(node.GetRawText(), (await Node(N3)).GetRawText());
    }

    [Fact]
    public async Task CorrectingRecomputesTheSpendFigureForTheNewCategory()
    {
        await Reset();
        var seed = Seed;
        var stored = (await Repo.GetNodeAsync(Ctx, N3, Ct))!;
        var invoice = (await Repo.GetInvoiceAsync(Ctx, stored.InvoiceId, Ct))!;

        var node = await Ok(await Review(N3, "plastics", "wire_harness"));

        var expected = Estimate.SpendBasedKg(invoice.Extracted.Total, invoice.IssueDate, seed.CategoryOf("plastics").SectorKey, Table(seed));
        Assert.Equal("corrected", node.GetProperty("classification").GetProperty("review").GetString());
        Assert.Equal("plastics", node.GetProperty("classification").GetProperty("categoryId").GetString());
        Assert.Equal("Plastic parts", node.GetProperty("classification").GetProperty("categoryLabel").GetString());
        Assert.Equal(expected.Kg, node.GetProperty("spend").GetProperty("kg").GetDecimal());
        Assert.Equal(expected.Kg, node.GetProperty("carbonKg").GetDecimal());
        Assert.Equal(expected.Inputs[0].RowLabel, node.GetProperty("spend").GetProperty("factors")[0].GetProperty("rowLabel").GetString());
        Assert.NotEqual(stored.SpendKg, expected.Kg);
        // Cost and lead time follow the lane and the mass, not the category.
        Assert.Equal(stored.CostEur, node.GetProperty("costEur").GetDecimal());
        Assert.Equal(stored.LeadDays, node.GetProperty("leadDays").GetInt32());
    }

    [Fact]
    public async Task CorrectingAVerifiedNodeRecomputesItsActivityFigureWithTheNewMaterial()
    {
        await Reset();
        var seed = Seed;
        var n2 = (await Repo.GetNodeAsync(Ctx, N2, Ct))!;
        var lane = seed.Lanes.Single(l => l.Id == n2.LaneId);

        var node = await Ok(await Review(N2, "connectors", "pcb"));

        var expected = Estimate.ActivityBasedKg(
            new ActivityInput(n2.MassT, lane.DistanceKm[TransportMode.Sea]!.Value, TransportMode.Sea, seed.CategoryOf("connectors").MaterialKey, EnergySource.Grid),
            Table(seed));
        Assert.Equal("VERIFIED", node.GetProperty("status").GetString());
        Assert.Equal("activity", node.GetProperty("basis").GetString());
        Assert.Equal(expected.TotalKg, node.GetProperty("activity").GetProperty("kg").GetDecimal());
        Assert.Equal(expected.TotalKg, node.GetProperty("carbonKg").GetDecimal());
        Assert.NotEqual(n2.ActivityKg, expected.TotalKg);
        Assert.Equal(expected.Inputs[1].RowLabel, node.GetProperty("activity").GetProperty("factors")[1].GetProperty("rowLabel").GetString());
        // A category from an invoice code can be corrected too.
        Assert.Equal("corrected", node.GetProperty("classification").GetProperty("review").GetString());
    }

    [Fact]
    public async Task ACorrectionCanBeChangedAgainAndStaysCorrected()
    {
        await Reset();
        await Ok(await Review(N3, "plastics", "wire_harness"));

        var again = await Ok(await Review(N3, "connectors", "plastics"));
        var kept = await Ok(await Review(N3, "connectors", "connectors"));

        Assert.Equal("connectors", again.GetProperty("classification").GetProperty("categoryId").GetString());
        Assert.Equal("corrected", again.GetProperty("classification").GetProperty("review").GetString());
        Assert.Equal("corrected", kept.GetProperty("classification").GetProperty("review").GetString());
    }

    [Fact]
    public async Task UnclassifiedIsAnAllowedChoice()
    {
        await Reset();

        var node = await Ok(await Review(N3, Ids.Unclassified, "wire_harness"));

        Assert.Equal(Ids.Unclassified, node.GetProperty("classification").GetProperty("categoryId").GetString());
        Assert.Equal("corrected", node.GetProperty("classification").GetProperty("review").GetString());
    }

    [Fact]
    public async Task AStaleCategoryIs409AndChangesNothing()
    {
        await Reset();
        var before = (await Node(N3)).GetRawText();
        var seq = Bus.LastSeq(Ids.DemoWorkspace);
        var audit = (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct)).Count;

        await AssertProblem(await Review(N3, "plastics", "pcb"), HttpStatusCode.Conflict, "CategoryChanged");

        Assert.Equal(before, (await Node(N3)).GetRawText());
        Assert.Equal(seq, Bus.LastSeq(Ids.DemoWorkspace));
        Assert.Equal(audit, (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct)).Count);
    }

    [Fact]
    public async Task TheSecondOfTwoReviewsFromTheSamePageIs409()
    {
        await Reset();

        await Ok(await Review(N3, "plastics", "wire_harness"));
        var second = await Review(N3, "connectors", "wire_harness");

        await AssertProblem(second, HttpStatusCode.Conflict, "CategoryChanged");
        Assert.Equal("plastics", (await Node(N3)).GetProperty("classification").GetProperty("categoryId").GetString());
    }

    [Theory]
    [InlineData("steel")]
    [InlineData("")]
    [InlineData("Plastics")]
    public async Task ACategoryOutsideTheListIs422(string categoryId)
    {
        await Reset();
        var before = (await Node(N3)).GetRawText();

        await AssertProblem(await Review(N3, categoryId, "wire_harness"), HttpStatusCode.UnprocessableEntity, "CategoryNotAllowed");

        Assert.Equal(before, (await Node(N3)).GetRawText());
    }

    [Fact]
    public async Task AnUnknownNodeIs404()
    {
        await Reset();

        // N1 is the demo invoice, not on the graph until imported.
        await AssertProblem(await Review(TestNodes.N1Id, "plastics", "zinc_diecast"), HttpStatusCode.NotFound, "NodeNotFound");
    }

    [Fact]
    public async Task AReviewAppendsAnAuditEntryAndTheChainStillVerifies()
    {
        await Reset();

        await Ok(await Review(N3, "plastics", "wire_harness"));

        var entry = (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct))[^1];
        Assert.Equal("classification.reviewed", entry.Action);
        Assert.Equal("buyer", entry.Actor);
        var payload = entry.Payload;
        Assert.Equal(N3.ToString("D"), payload["nodeId"]!.GetValue<string>());
        Assert.Equal("corrected", payload["review"]!.GetValue<string>());
        Assert.Equal("wire_harness", payload["fromCategoryId"]!.GetValue<string>());
        Assert.Equal("plastics", payload["toCategoryId"]!.GetValue<string>());
        Assert.Equal("cache", payload["fromSource"]!.GetValue<string>());
        Assert.Equal("0.9", payload["fromConfidence"]!.GetValue<string>());
        Assert.Equal(6, payload.AsObject().Count);
        var reviewedAt = (await Node(N3)).GetProperty("classification").GetProperty("reviewedAt").GetDateTimeOffset();
        Assert.Equal(entry.Ts, reviewedAt);

        var chain = await Json(await _client.PostAsync("/api/audit/verify", null, Ct));
        Assert.True(chain.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task AReviewPublishesNodeUpdatedWithFreshKpisThenTheAuditEntry()
    {
        await Reset();
        var seq = Bus.LastSeq(Ids.DemoWorkspace);

        var node = await Ok(await Review(N3, "plastics", "wire_harness"));

        var events = Bus.ReplaySince(Ids.DemoWorkspace, seq).Events;
        Assert.Equal(2, events.Count);
        var updated = Assert.IsType<NodeUpdated>(events[0]);
        Assert.Equal(N3, updated.Node.Id);
        Assert.Equal(ClassificationReview.Corrected, updated.Node.Classification.Review);
        var graph = await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct);
        Assert.Equal(graph.GetProperty("kpis").GetProperty("totalKg").GetDecimal(), updated.Kpis.TotalKg);
        Assert.Equal(node.GetProperty("carbonKg").GetDecimal(), updated.Node.CarbonKg);
        Assert.Equal("classification.reviewed", Assert.IsType<AuditAppended>(events[1]).Entry.Action);
    }

    [Fact]
    public async Task TheSuppliersLinkStillWorksAfterACorrection()
    {
        await Reset();
        var link = await Json(await _client.PostAsJsonAsync("/api/request-link", new { nodeId = N3 }, Ct));
        var token = Uri.UnescapeDataString(new Uri(link.GetProperty("url").GetString()!).Query["?t=".Length..]);

        await Ok(await Review(N3, "plastics", "wire_harness"));

        // The link is bound to the invoice, which a review does not touch.
        var context = await _client.GetAsync($"/api/verify/context?t={Uri.EscapeDataString(token)}", Ct);
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        var submit = await _client.PostAsJsonAsync(
            "/api/verify",
            new { t = token, transportMode = "road", energySource = "grid", acceptedTerms = Seed.SupplierTerms.Sha256 },
            Ct);
        Assert.Equal("VERIFIED", (await Ok(submit)).GetProperty("status").GetString());

        var node = await Node(N3);
        var classification = node.GetProperty("classification");
        Assert.Equal("plastics", classification.GetProperty("categoryId").GetString());
        Assert.Equal("buyer", classification.GetProperty("source").GetString());
        Assert.Equal("corrected", classification.GetProperty("review").GetString());
        // The supplier's answer is computed with the buyer's category.
        var seed = Seed;
        var stored = (await Repo.GetNodeAsync(Ctx, N3, Ct))!;
        var lane = seed.Lanes.Single(l => l.Id == stored.LaneId);
        var material = seed.CategoryOf("plastics").MaterialKey;
        var expected = Estimate.ActivityBasedKg(
            new ActivityInput(stored.MassT, lane.DistanceKm[TransportMode.Road]!.Value, TransportMode.Road, material, EnergySource.Grid), Table(seed));
        Assert.Equal(expected.TotalKg, node.GetProperty("activity").GetProperty("kg").GetDecimal());
        Assert.Equal(
            Table(seed).Lookup(FactorKind.Material, material).RowLabel,
            node.GetProperty("activity").GetProperty("factors")[1].GetProperty("rowLabel").GetString());
    }
}
