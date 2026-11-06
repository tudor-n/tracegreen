using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>Buyer corrections of a category (F6). Each test starts from the seed state through the reset route.</summary>
public class ClassificationEndpointTests : IClassFixture<TestWebAppFactory>
{
    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly string N1Xml = File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml"));
    private static readonly string AdversarialXml = File.ReadAllText(TestPaths.Invoice("adversarial.xml"));

    // N1 is EUR 48,000 dated 2026-10-28 (see IngestEndpointTests): the ECB rate 1.1193 USD/EUR, CPI-U 0.9090, then the
    // sector factor of the category: 0.462 for die-cast (331523) and 0.098 for bare printed circuit boards (334412).
    private const decimal N1UsdAt2022 = 48000m * 1.1193m * 0.9090m;
    private const decimal N1AsPcbKg = N1UsdAt2022 * 0.098m;

    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;

    public ClassificationEndpointTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Reset() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/dev/reset", null, Ct)).StatusCode);

    private async Task<JsonElement> Import(string xml)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        form.Add(file, "file", "invoice.xml");
        var response = await _client.PostAsync("/api/ingest/ubl", form, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("node");
    }

    private Task<HttpResponseMessage> Correct(Guid nodeId, string categoryId) =>
        _client.PutAsJsonAsync($"/api/nodes/{nodeId}/classification", new { categoryId }, Ct);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<int> AuditCount() =>
        (await _factory.Services.GetRequiredService<IRepository>().ListAuditAsync(new Ctx(Ids.DemoWorkspace, "test"), 0, 10_000, Ct)).Count;

    [Fact]
    public async Task CorrectingAnEstimatedNodeRecomputesItsSpendEstimateWithTheNewSector()
    {
        await Reset();
        var nodeId = (await Import(N1Xml)).GetProperty("id").GetGuid();

        var response = await Correct(nodeId, "pcb");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = await Json(response);
        Assert.Equal("ESTIMATED", node.GetProperty("status").GetString());
        Assert.Equal(N1AsPcbKg, node.GetProperty("carbonKg").GetDecimal());
        var classification = node.GetProperty("classification");
        Assert.Equal("pcb", classification.GetProperty("categoryId").GetString());
        Assert.Equal("correction", classification.GetProperty("source").GetString());
        Assert.False(classification.GetProperty("aiMapped").GetBoolean());
        Assert.Equal("334412 Bare Printed Circuit Board Manufacturing", node.GetProperty("spend").GetProperty("factors")[0].GetProperty("rowLabel").GetString());
    }

    [Fact]
    public async Task ACorrectionPublishesNodeUpdatedWithFreshKpisThenItsAuditEntry()
    {
        await Reset();
        var nodeId = (await Import(N1Xml)).GetProperty("id").GetGuid();
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var before = bus.LastSeq(Ids.DemoWorkspace);
        var totalBefore = (await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct)).GetProperty("kpis").GetProperty("totalKg").GetDecimal();

        await Correct(nodeId, "pcb");

        var events = bus.ReplaySince(Ids.DemoWorkspace, before).Events;
        Assert.Collection(events,
            e =>
            {
                var updated = Assert.IsType<NodeUpdated>(e);
                Assert.Equal(nodeId, updated.Node.Id);
                Assert.Equal(totalBefore - N1UsdAt2022 * 0.462m + N1AsPcbKg, updated.Kpis.TotalKg);
            },
            e =>
            {
                var entry = Assert.IsType<AuditAppended>(e).Entry;
                Assert.Equal("classification.corrected", entry.Action);
                Assert.Equal("zinc_diecast", entry.Payload!["from"]!.GetValue<string>());
                Assert.Equal("pcb", entry.Payload!["to"]!.GetValue<string>());
                Assert.Equal("invoice", entry.Payload!["previousSource"]!.GetValue<string>());
            });
    }

    [Fact]
    public async Task CorrectingAVerifiedNodeKeepsItsStatusAndAnswersAndFollowsTheNewMaterial()
    {
        await Reset();
        var before = await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct);
        var n2Before = before.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == N2);

        var response = await Correct(N2, "connectors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = await Json(response);
        Assert.Equal("VERIFIED", node.GetProperty("status").GetString());
        Assert.Equal("activity", node.GetProperty("basis").GetString());
        Assert.Equal(n2Before.GetProperty("transportMode").GetString(), node.GetProperty("transportMode").GetString());
        Assert.Equal(n2Before.GetProperty("energySource").GetString(), node.GetProperty("energySource").GetString());
        var materialRows = node.GetProperty("activity").GetProperty("factors").EnumerateArray().Select(f => f.GetProperty("rowLabel").GetString()).ToList();
        Assert.DoesNotContain("Electrical items: printed circuit boards (placeholder)", materialRows);
    }

    [Fact]
    public async Task SendingTheSameCorrectionAgainChangesNothing()
    {
        await Reset();
        Assert.Equal(HttpStatusCode.OK, (await Correct(N2, "connectors")).StatusCode);
        var audit = await AuditCount();

        var again = await Correct(N2, "connectors");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(audit, await AuditCount());
    }

    [Fact]
    public async Task ACorrectionIsReusedForTheSameDescriptionOnTheNextImport()
    {
        await Reset();
        var first = await Import(AdversarialXml);
        Assert.Equal("cache", first.GetProperty("classification").GetProperty("source").GetString());
        await Correct(first.GetProperty("id").GetGuid(), "connectors");

        var second = await Import(AdversarialXml.Replace("OH-2026-ADV1", "OH-2026-ADV2"));

        var classification = second.GetProperty("classification");
        Assert.Equal("connectors", classification.GetProperty("categoryId").GetString());
        Assert.Equal("correction", classification.GetProperty("source").GetString());
    }

    [Fact]
    public async Task ACategoryOutsideTheAllowListIs422()
    {
        var response = await Correct(N2, "bogus");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("CategoryNotAllowed", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnUnknownNodeIs404()
    {
        var response = await Correct(Guid.NewGuid(), "pcb");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NodeNotFound", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task CategoriesListTheAllowList()
    {
        var categories = await _client.GetFromJsonAsync<JsonElement>("/api/categories", Ct);

        var ids = categories.EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.Equal(["zinc_diecast", "pcb", "wire_harness", "packaging_paper", "connectors", "plastics", "UNCLASSIFIED"], ids);
        Assert.All(categories.EnumerateArray(), c => Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("label").GetString())));
    }
}
