using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TraceGreen.Tests.Api;

public class HealthEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public HealthEndpointTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task HealthReportsOkAndMemoryMode()
    {
        var client = _factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/api/health", TestContext.Current.CancellationToken);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal("memory", body.GetProperty("mode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task HealthReportsRepositoryAndSeedState()
    {
        var client = _factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/api/health", TestContext.Current.CancellationToken);
        Assert.Equal("ok", body.GetProperty("db").GetString());
        Assert.Equal(4, body.GetProperty("seedNodes").GetInt32());
    }

    [Fact]
    public async Task HealthReportsAiWithoutHangingWhenTheServiceIsDown()
    {
        var client = _factory.CreateClient();
        var started = DateTime.UtcNow;
        var body = await client.GetFromJsonAsync<JsonElement>("/api/health", TestContext.Current.CancellationToken);

        Assert.StartsWith("error: ", body.GetProperty("ai").GetString());
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HealthReportsTheRequestOriginAsLoopbackBaseUrl()
    {
        var client = _factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/api/health", TestContext.Current.CancellationToken);
        var baseUrl = body.GetProperty("baseUrl");

        Assert.Equal("request", baseUrl.GetProperty("source").GetString());
        Assert.Equal("http://localhost", baseUrl.GetProperty("url").GetString());
        Assert.True(baseUrl.GetProperty("loopback").GetBoolean());
    }

    [Fact]
    public async Task HealthPrefersTheForwardedHost()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Forwarded-Host", "192.168.137.1:3000");
        request.Headers.Add("X-Forwarded-Proto", "http");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var baseUrl = body.GetProperty("baseUrl");

        Assert.Equal("forwarded", baseUrl.GetProperty("source").GetString());
        Assert.Equal("http://192.168.137.1:3000", baseUrl.GetProperty("url").GetString());
        Assert.False(baseUrl.GetProperty("loopback").GetBoolean());
    }

    [Fact]
    public async Task OpenApiDocumentListsHealth()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Contains("/api/health", json);
    }

    [Fact]
    public async Task OpenApiDocumentTypesTheSliceOneRoutesAndTheEventUnion()
    {
        var client = _factory.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);
        var paths = doc.GetProperty("paths");
        var schemas = doc.GetProperty("components").GetProperty("schemas");

        Assert.Equal("#/components/schemas/GraphSnapshot", Ref(paths, "/api/graph", "get", "200", "application/json"));
        Assert.Equal("#/components/schemas/IngestResult", Ref(paths, "/api/ingest/ubl", "post", "201", "application/json"));
        Assert.Equal("#/components/schemas/ApiProblem", Ref(paths, "/api/ingest/ubl", "post", "413", "application/problem+json"));
        Assert.Equal("#/components/schemas/IngestResult", Ref(paths, "/api/ingest/demo", "post", "201", "application/json"));
        Assert.Equal("#/components/schemas/DomainEvent", Ref(paths, "/api/events", "get", "200", "text/event-stream"));
        Assert.True(paths.GetProperty("/api/dev/reset").GetProperty("post").GetProperty("responses").TryGetProperty("204", out _));

        var mapping = schemas.GetProperty("DomainEvent").GetProperty("discriminator").GetProperty("mapping");
        Assert.Equal(
            ["audit.appended", "graph.reset", "node.created", "node.updated", "node.verified", "scenario.saved"],
            mapping.EnumerateObject().Select(p => p.Name).Order());
        Assert.Contains("type", schemas.GetProperty("DomainEventNodeCreated").GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.True(schemas.GetProperty("HealthResponse").GetProperty("properties").TryGetProperty("baseUrl", out _));
    }

    private static string? Ref(JsonElement paths, string path, string method, string status, string mediaType) =>
        paths.GetProperty(path).GetProperty(method).GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty(mediaType).GetProperty("schema").GetProperty("$ref").GetString();
}
