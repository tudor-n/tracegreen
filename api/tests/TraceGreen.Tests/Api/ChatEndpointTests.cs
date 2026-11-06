using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>A chat model whose answer the test sets, recording what it was sent.</summary>
public sealed class ScriptedChatAi : IAiAssist
{
    public Func<ChatInput, Task<ChatOutcome>> Answer { get; set; } = _ => Task.FromResult(new ChatOutcome(false, "", [], "stub"));

    public List<ChatInput> Sent { get; } = [];

    public Task<ChatOutcome> ChatAsync(ChatInput input, CancellationToken ct)
    {
        Sent.Add(input);
        return Answer(input);
    }

    public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, "scripted"));
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
}
