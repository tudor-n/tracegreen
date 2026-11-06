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
    public async Task SuggestPostsTheNodeAndCandidatesWithoutFiguresAndReadsTheProvider()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"suggestions":[{"alternativeId":"N2-alt","rationale":"Same boards, made in Romania.","label":"Banat Circuits SRL"}],"provider":"openai:model-a"}""");

        var outcome = await Client(handler).SuggestAsync(
            new SuggestNodeInput("Printed circuit boards", "4-layer FR4 boards", "TW", "Taoyuan, TW", "sea", "VERIFIED"),
            [new SuggestCandidateInput("N2-alt", "Banat Circuits SRL", "RO", "Timisoara, RO", "road")],
            CancellationToken.None);

        Assert.Equal("/suggest", handler.Request!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var node = body.RootElement.GetProperty("node");
        Assert.Equal("Printed circuit boards", node.GetProperty("categoryLabel").GetString());
        Assert.Equal("sea", node.GetProperty("mode").GetString());
        Assert.Equal(
            ["categoryLabel", "lineDescription", "country", "origin", "mode", "status"],
            node.EnumerateObject().Select(p => p.Name));
        var candidate = body.RootElement.GetProperty("candidates")[0];
        Assert.Equal(["alternativeId", "label", "country", "origin", "mode"], candidate.EnumerateObject().Select(p => p.Name));

        Assert.Equal("openai:model-a", outcome.Provider);
        Assert.Equal("N2-alt", Assert.Single(outcome.Suggestions).AlternativeId);
    }

    [Fact]
    public async Task ChatPostsTheQuestionHistoryAndFactsAndReadsTheAnswer()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"answerable":true,"text":"{{S1.name}} leads.","factIds":["S1.name"],"provider":"ollama:local"}""");
        var assist = new HttpAiAssist(new HttpClient(handler) { BaseAddress = new Uri("http://ai:8000") });

        var outcome = await assist.ChatAsync(
            new ChatInput("Who leads?", ["Before?"], [new ChatFactInput("S1.name", "supplier S1: name", "Taiwan PCB Ltd.")]),
            CancellationToken.None);

        Assert.Equal("/chat", handler.Request!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("Who leads?", body.RootElement.GetProperty("question").GetString());
        Assert.Equal("Before?", body.RootElement.GetProperty("history")[0].GetString());
        var fact = body.RootElement.GetProperty("facts")[0];
        Assert.Equal(["id", "about", "value"], fact.EnumerateObject().Select(p => p.Name));
        Assert.Equal((true, "{{S1.name}} leads.", "ollama:local"), (outcome.Answerable, outcome.Text, outcome.Provider));
        Assert.Equal(["S1.name"], outcome.FactIds);
    }

    private static HttpAiAssist Assist(CapturingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ai:8000") });

    [Fact]
    public async Task ChatSendsTheChosenModel()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"answerable":false,"text":"","factIds":[],"provider":"none"}""");

        await Assist(handler).ChatAsync(new ChatInput("Who leads?", [], [], "ollama:local:3b"), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("ollama:local:3b", body.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("unknown_model", ChatModelProblem.Unknown)]
    [InlineData("model_not_ready", ChatModelProblem.NotReady)]
    public async Task ChatReadsARefusedModel(string code, ChatModelProblem problem)
    {
        var handler = new CapturingHandler(HttpStatusCode.UnprocessableEntity, $$$"""{"detail":{"code":"{{{code}}}","message":"no"}}""");

        var outcome = await Assist(handler).ChatAsync(new ChatInput("Who leads?", [], [], "x:y"), CancellationToken.None);

        Assert.Equal(problem, outcome.ModelProblem);
        Assert.False(outcome.Answerable);
    }

    [Fact]
    public async Task ChatModelsReadsTheList()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """
            {"models":[
              {"id":"anthropic:claude-x","provider":"anthropic","model":"claude-x","ready":false,"reason":"ANTHROPIC_API_KEY not set","automatic":true},
              {"id":"ollama:qwen2.5:14b","provider":"ollama","model":"qwen2.5:14b","ready":true,"reason":null,"automatic":false}
            ]}
            """);

        var models = await Assist(handler).ChatModelsAsync(CancellationToken.None);

        Assert.Equal("/chat/models", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal(
            [
                new ChatModelInfo("anthropic:claude-x", "anthropic", "claude-x", false, "ANTHROPIC_API_KEY not set", true),
                new ChatModelInfo("ollama:qwen2.5:14b", "ollama", "qwen2.5:14b", true, null, false),
            ],
            models);
    }

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
