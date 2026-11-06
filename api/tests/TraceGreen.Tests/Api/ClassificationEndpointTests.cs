using System.Net.Http.Json;
using System.Text.Json;

namespace TraceGreen.Tests.Api;

/// <summary>The classification review (F6) over HTTP: the review state on every node and the review route.</summary>
public class ClassificationEndpointTests : IClassFixture<TestWebAppFactory>
{
    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly Guid N3 = Guid.Parse("11111111-1111-4111-8111-000000000003");

    private readonly HttpClient _client;

    public ClassificationEndpointTests(TestWebAppFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> Node(Guid id)
    {
        var graph = await _client.GetFromJsonAsync<JsonElement>("/api/graph", Ct);
        return graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task AnAiMappedNodeWaitsForReviewAndAnInvoiceCodedOneDoesNot()
    {
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
}
