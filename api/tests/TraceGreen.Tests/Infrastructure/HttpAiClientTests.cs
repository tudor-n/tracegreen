using System.Net;
using System.Text;
using System.Text.Json;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Infrastructure.Ai;

namespace TraceGreen.Tests.Infrastructure;

public class HttpAiClientTests
{
    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpAiClient Client(CapturingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ai:8000") }, new AiOptions(new Uri("http://ai:8000"), TimeSpan.FromSeconds(10)));

    [Fact]
    public async Task ClassifyPostsTheContractAndParsesTheAnswer()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"classifications":[{"lineId":"1","categoryId":"pcb","confidence":0.91,"rationale":"board","source":"model"}],"provider":"chain(cached,stub)"}""");

        var result = await Client(handler).ClassifyAsync(
            [new ClassificationInput("1", "4-layer PCB", 900m, "KGM", "31710000", "CPV")], CancellationToken.None);

        Assert.Equal("/classify", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var line = body.RootElement.GetProperty("lines")[0];
        Assert.Equal("1", line.GetProperty("lineId").GetString());
        Assert.Equal("4-layer PCB", line.GetProperty("description").GetString());
        Assert.Equal("CPV", line.GetProperty("classificationListId").GetString());

        var c = Assert.Single(result);
        Assert.Equal("pcb", c.CategoryId);
        Assert.Equal(0.91, c.Confidence);
        Assert.Equal(ClassificationSource.Model, c.Source);
    }

    [Fact]
    public async Task ServerErrorsThrowSoTheCallerCanFallBack()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).ClassifyAsync([new ClassificationInput("1", "x", null, null, null, null)], CancellationToken.None));
    }

    [Fact]
    public async Task HealthReadsTheServiceStatus()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"ok":true,"provider":"chain(cached,stub)","cacheEntries":6,"modelConfigured":false}""");

        var health = await Client(handler).HealthAsync(CancellationToken.None);

        Assert.True(health.Ok);
        Assert.Equal("/health", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Contains("chain", health.Detail);
    }
}
