using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Chat;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>An AI service for the longer tasks whose answers the test sets (chat and PDF extraction), recording what it was sent.</summary>
public sealed class ScriptedChatAi : IAiAssist
{
    public Func<byte[], Task<ExtractOutcome>> Extract { get; set; } = _ => Task.FromResult(ExtractOutcome.Failed(ExtractFailure.Unavailable));

    public List<byte[]> PdfsSent { get; } = [];

    public Task<ExtractOutcome> ExtractPdfAsync(byte[] pdf, CancellationToken ct)
    {
        PdfsSent.Add(pdf);
        return Extract(pdf);
    }

    public Func<ChatInput, Task<ChatOutcome>> Answer { get; set; } = _ => Task.FromResult(new ChatOutcome(false, "", [], "stub"));

    public List<ChatInput> Sent { get; } = [];

    public Task<ChatOutcome> ChatAsync(ChatInput input, CancellationToken ct)
    {
        Sent.Add(input);
        return Answer(input);
    }

    public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, "scripted"));

    public Func<Task<IReadOnlyList<ChatModelInfo>>> Models { get; set; } = () => Task.FromResult<IReadOnlyList<ChatModelInfo>>([]);

    public Task<IReadOnlyList<ChatModelInfo>> ChatModelsAsync(CancellationToken ct) => Models();
}

public sealed class ChatWebAppFactory : TestWebAppFactory
{
    public ScriptedChatAi Ai { get; } = new();

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IAiAssist>(Ai);
}

/// <summary>The data chat (F13): the model writes the sentence, the API checks it and fills every value in from the data.</summary>
public class ChatEndpointTests(ChatWebAppFactory factory) : IClassFixture<ChatWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> Ask(string? question, params string[] history) =>
        _client.PostAsJsonAsync("/api/chat", new { question, history }, Ct);

    private async Task<JsonElement> Answer(string question)
    {
        var response = await Ask(question);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private void Model(string text, params string[] ids) =>
        factory.Ai.Answer = _ => Task.FromResult(new ChatOutcome(true, text, ids, "openai:model-a"));

    private async Task<IReadOnlyList<SupplierNode>> Nodes() =>
        await factory.Services.GetRequiredService<IRepository>().ListNodesAsync(new Ctx(Ids.DemoWorkspace, "test"), Ct);

    [Fact]
    public async Task ACheckedAnswerComesBackAsSegmentsWithEveryValueFromTheData()
    {
        Model("The largest emitter is {{S1.name}}, with {{S1.carbon}}.", "S1.name", "S1.carbon");
        var largest = (await Nodes()).MaxBy(n => n.CarbonKg)!;

        var body = await Answer("Who emits the most?");

        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal("openai:model-a", body.GetProperty("provider").GetString());
        var segments = body.GetProperty("segments").EnumerateArray().ToList();
        Assert.Equal("The largest emitter is ", segments[0].GetProperty("text").GetString());
        Assert.Equal("S1.name", segments[1].GetProperty("factId").GetString());
        var facts = body.GetProperty("facts").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetString()!);
        Assert.Equal(largest.Label, facts["S1.name"].GetProperty("value").GetString());
        Assert.Equal(largest.CarbonKg.ToString(CultureInfo.InvariantCulture), facts["S1.carbon"].GetProperty("value").GetString());
        Assert.Equal("carbonKg", facts["S1.carbon"].GetProperty("kind").GetString());
        Assert.Equal(largest.Label, facts["S1.carbon"].GetProperty("subject").GetString());
        Assert.Equal(largest.Id, facts["S1.carbon"].GetProperty("nodeId").GetGuid());
    }

    [Fact]
    public async Task TheModelIsSentTheQuestionThreeEarlierQuestionsAndTheFactsButNoAnswer()
    {
        factory.Ai.Sent.Clear();
        Model("{{total.carbon}}.", "total.carbon");

        await Ask("And the total?", "one", "two", "three", "four");

        var input = Assert.Single(factory.Ai.Sent);
        Assert.Equal("And the total?", input.Question);
        Assert.Equal(["two", "three", "four"], input.History);
        Assert.Contains(input.Facts, f => f.Id == "share.confirmed" && f.About.Contains("by emissions, not by node count"));
    }

    [Theory]
    [InlineData("The total is 19.4 t.")]
    [InlineData("{{S1.name}} could switch to Arad Castings SRL.")]
    [InlineData("{{S9.carbon}} is the largest.")]
    public async Task AnAnswerThatFailsTheCheckIsNotAnswered(string text)
    {
        Model(text);

        var body = await Answer("Anything");

        Assert.Equal("notAnswered", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("segments").EnumerateArray());
        Assert.Empty(body.GetProperty("facts").EnumerateArray());
    }

    [Fact]
    public async Task ARefusedAnswerIsAskedForOnceMoreFromTheSameModelWithTheRule()
    {
        factory.Ai.Sent.Clear();
        factory.Ai.Answer = input => Task.FromResult(input.Retry is null
            ? new ChatOutcome(true, "The largest emitter is {{S2.name}}.", ["S2.name"], "ollama:local:3b")
            : new ChatOutcome(true, "The largest emitter is {{S1.name}}.", ["S1.name"], "ollama:local:3b"));

        var body = await Answer("Who emits the most?");

        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal(2, factory.Ai.Sent.Count);
        var retry = factory.Ai.Sent[1];
        Assert.Equal("ollama:local:3b", retry.Model);
        Assert.Equal("The largest emitter is {{S2.name}}.", retry.Retry!.Text);
        Assert.Equal(AnswerCheck.RetryHint("a largest claim the data does not hold"), retry.Retry.Problem);
    }

    [Fact]
    public async Task AnAnswerRefusedTwiceIsNotAnswered()
    {
        factory.Ai.Sent.Clear();
        Model("The total is 19.4 t.");

        var body = await Answer("What is the total?");

        Assert.Equal("notAnswered", body.GetProperty("status").GetString());
        Assert.Equal(2, factory.Ai.Sent.Count);
    }

    [Fact]
    public async Task ALongAnswerRefusedTwiceIsShownWithoutItsFalseSentence()
    {
        Model("The largest emitter is {{S1.name}}. Compare its transport in What if. The next largest is {{S3.name}}.", "S1.name", "S3.name");

        var body = await Answer("How can I reduce my emissions?");

        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal(["S1.name"], body.GetProperty("facts").EnumerateArray().Select(f => f.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AGeneralAnswerComesBackAsTextWithNoFacts()
    {
        const string text = "I can tell you who emits the most and what a change of transport would do.";
        Model(text);

        var body = await Answer("I need help");

        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal(text, Assert.Single(body.GetProperty("segments").EnumerateArray()).GetProperty("text").GetString());
        Assert.Empty(body.GetProperty("facts").EnumerateArray());
    }

    [Fact]
    public async Task AQuestionTheModelCannotAnswerFromTheDataIsNotAnswered()
    {
        factory.Ai.Answer = _ => Task.FromResult(new ChatOutcome(false, "", [], "openai:model-a"));

        Assert.Equal("notAnswered", (await Answer("What will steel cost next year?")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task NoModelOrAFailingServiceIsUnavailable()
    {
        factory.Ai.Answer = _ => Task.FromResult(new ChatOutcome(false, "", [], "stub"));
        Assert.Equal("unavailable", (await Answer("Who emits most?")).GetProperty("status").GetString());

        factory.Ai.Answer = _ => throw new HttpRequestException("down");
        Assert.Equal("unavailable", (await Answer("Who emits most?")).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AnEmptyQuestionIs422(string? question)
    {
        var response = await Ask(question);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("InvalidQuestion", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnOverLongQuestionIs422() =>
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Ask(new string('a', 301))).StatusCode);

    [Fact]
    public async Task TheModelListIsTheAiServicesInItsOrderWithWhyOneIsNotReady()
    {
        factory.Ai.Models = () => Task.FromResult<IReadOnlyList<ChatModelInfo>>(
        [
            new ChatModelInfo("anthropic:claude-x", "anthropic", "claude-x", false, "ANTHROPIC_API_KEY not set", true),
            new ChatModelInfo("ollama:local:3b", "ollama", "local:3b", true, null, false),
        ]);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/chat/models", Ct);

        Assert.True(body.GetProperty("available").GetBoolean());
        var models = body.GetProperty("models").EnumerateArray().ToList();
        Assert.Equal(["anthropic:claude-x", "ollama:local:3b"], models.Select(m => m.GetProperty("id").GetString()));
        Assert.False(models[0].GetProperty("ready").GetBoolean());
        Assert.Equal("ANTHROPIC_API_KEY not set", models[0].GetProperty("reason").GetString());
        Assert.Equal("local:3b", models[1].GetProperty("model").GetString());
        Assert.Equal("ollama", models[1].GetProperty("provider").GetString());
        Assert.Equal([true, false], models.Select(m => m.GetProperty("automatic").GetBoolean()));
    }

    [Fact]
    public async Task AnAiServiceThatDoesNotAnswerListsNoModelAndSaysSo()
    {
        factory.Ai.Models = () => throw new HttpRequestException("down");

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/chat/models", Ct);

        Assert.False(body.GetProperty("available").GetBoolean());
        Assert.Empty(body.GetProperty("models").EnumerateArray());
    }

    [Fact]
    public async Task TheChosenModelIsSentWithTheQuestionAndNoneMeansInOrder()
    {
        factory.Ai.Sent.Clear();
        Model("{{total.carbon}}.", "total.carbon");

        await _client.PostAsJsonAsync("/api/chat", new { question = "Total?", model = " ollama:local:3b " }, Ct);
        await _client.PostAsJsonAsync("/api/chat", new { question = "Total?", model = "" }, Ct);
        await Ask("Total?");

        Assert.Equal(["ollama:local:3b", null, null], factory.Ai.Sent.Select(i => i.Model));
    }

    [Theory]
    [InlineData(ChatModelProblem.Unknown, "UnknownModel")]
    [InlineData(ChatModelProblem.NotReady, "ModelNotReady")]
    public async Task AModelTheAiServiceRefusesIs422WithItsCode(ChatModelProblem problem, string code)
    {
        factory.Ai.Answer = _ => Task.FromResult(new ChatOutcome(false, "", [], "stub", problem));

        var response = await _client.PostAsJsonAsync("/api/chat", new { question = "Total?", model = "openai:model-z" }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnOverLongModelIdIs422WithoutAskingTheAi()
    {
        factory.Ai.Sent.Clear();

        var response = await _client.PostAsJsonAsync("/api/chat", new { question = "Total?", model = new string('m', 121) }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("UnknownModel", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        Assert.Empty(factory.Ai.Sent);
    }
}
