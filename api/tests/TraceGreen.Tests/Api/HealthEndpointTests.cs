using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TraceGreen.Tests.Api;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
    public async Task OpenApiDocumentListsHealth()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Contains("/api/health", json);
    }
}
