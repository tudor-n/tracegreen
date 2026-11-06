using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>An AI client whose suggestions the test sets, and which records what it was sent.</summary>
public sealed class ScriptedSuggestAi : IAiClient
{
    public Func<SuggestNodeInput, IReadOnlyList<SuggestCandidateInput>, Task<SuggestOutcome>> Answer { get; set; } =
        (_, _) => Task.FromResult(new SuggestOutcome([], "stub"));

    public List<(SuggestNodeInput Node, IReadOnlyList<SuggestCandidateInput> Candidates)> Sent { get; } = [];

    public string Name => "scripted";

    public Task<IReadOnlyList<Classification>> ClassifyAsync(IReadOnlyList<ClassificationInput> lines, CancellationToken ct) =>
        throw new HttpRequestException("classification is not scripted");

    public Task<SuggestOutcome> SuggestAsync(SuggestNodeInput node, IReadOnlyList<SuggestCandidateInput> candidates, CancellationToken ct)
    {
        Sent.Add((node, candidates));
        return Answer(node, candidates);
    }

    public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, "scripted"));
}

public sealed class SuggestingWebAppFactory : TestWebAppFactory
{
    public ScriptedSuggestAi Ai { get; } = new();

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IAiClient>(Ai);
}

/// <summary>AI-suggested alternatives (F11): picks only among the node's curated alternatives, no figures.</summary>
public class SuggestionEndpointTests(SuggestingWebAppFactory factory) : IClassFixture<SuggestingWebAppFactory>
{
    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly Guid N4 = Guid.Parse("11111111-1111-4111-8111-000000000004");
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> Suggestions(Guid nodeId)
    {
        var response = await _client.GetAsync($"/api/nodes/{nodeId}/suggestions", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task OnlyTheNodesOwnAlternativesComeBackWithTheirReason()
    {
        factory.Ai.Answer = (_, _) => Task.FromResult(new SuggestOutcome(
            [
                new Suggestion("N2-alt", "Same printed circuit boards, made in Romania.", "An invented label"),
                new Suggestion("N1-alt", "Another category's alternative.", "x"),
                new Suggestion("made-up", "A supplier the model invented.", "Invented SRL"),
                new Suggestion("N2-alt", "A second pick of the same one.", "x"),
            ],
            "openai:model-a"));

        var body = await Suggestions(N2);

        Assert.Equal("openai:model-a", body.GetProperty("provider").GetString());
        var suggestion = Assert.Single(body.GetProperty("suggestions").EnumerateArray());
        Assert.Equal("N2-alt", suggestion.GetProperty("alternativeId").GetString());
        Assert.Equal("Same printed circuit boards, made in Romania.", suggestion.GetProperty("rationale").GetString());
        Assert.False(suggestion.TryGetProperty("label", out _));
    }

    [Fact]
    public async Task TheAiIsSentTheNodeAndItsCandidatesWithoutAFigure()
    {
        factory.Ai.Sent.Clear();
        factory.Ai.Answer = (_, _) => Task.FromResult(new SuggestOutcome([], "openai:model-a"));

        await Suggestions(N2);

        var (node, candidates) = Assert.Single(factory.Ai.Sent);
        Assert.Equal("Printed circuit boards", node.CategoryLabel);
        Assert.Equal("VERIFIED", node.Status);
        var candidate = Assert.Single(candidates);
        Assert.Equal(("N2-alt", "Banat Circuits SRL", "RO"), (candidate.AlternativeId, candidate.Label, candidate.Country));
    }

    [Fact]
    public async Task AReasonWithADigitIsDroppedButThePickStays()
    {
        factory.Ai.Answer = (_, _) => Task.FromResult(new SuggestOutcome([new Suggestion("N2-alt", "Cuts 40% of emissions.", "x")], "openai:model-a"));

        var suggestion = Assert.Single((await Suggestions(N2)).GetProperty("suggestions").EnumerateArray());

        Assert.Equal(JsonValueKind.Null, suggestion.GetProperty("rationale").ValueKind);
    }

    [Fact]
    public async Task AFailingAiSuggestsNothing()
    {
        factory.Ai.Answer = (_, _) => throw new HttpRequestException("down");

        var body = await Suggestions(N2);

        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
        Assert.Equal("stub", body.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task ANodeWithoutAlternativesNeverAsksTheAi()
    {
        factory.Ai.Sent.Clear();

        var body = await Suggestions(N4);

        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
        Assert.Equal("none", body.GetProperty("provider").GetString());
        Assert.Empty(factory.Ai.Sent);
    }

    [Fact]
    public async Task AnUnknownNodeIs404()
    {
        var response = await _client.GetAsync($"/api/nodes/{Guid.NewGuid()}/suggestions", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
