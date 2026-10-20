using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Infrastructure.Ai;

public sealed record AiOptions(Uri BaseUrl, TimeSpan Timeout);

/// <summary>HTTP adapter for the Python AI service (ai/app/schemas.py is the contract). Throws on transport errors; callers fall back.</summary>
public sealed class HttpAiClient(HttpClient http, AiOptions options) : IAiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Name => "http:" + options.BaseUrl;

    public async Task<IReadOnlyList<Classification>> ClassifyAsync(IReadOnlyList<ClassificationInput> lines, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("classify", new ClassifyRequest(lines), Json, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ClassifyResponse>(Json, ct)
            ?? throw new InvalidOperationException("Empty response from the AI service.");
        return body.Classifications
            .Select(c => new Classification(c.LineId, c.CategoryId, c.Confidence, c.Rationale ?? "", ParseSource(c.Source)))
            .ToList();
    }

    public async Task<IReadOnlyList<Suggestion>> SuggestAsync(SupplierNode node, IReadOnlyList<Alternative> candidates, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("suggest", new SuggestRequest(node, candidates), Json, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SuggestResponse>(Json, ct)
            ?? throw new InvalidOperationException("Empty response from the AI service.");
        return body.Suggestions;
    }

    public async Task<HealthStatus> HealthAsync(CancellationToken ct)
    {
        try
        {
            var body = await http.GetFromJsonAsync<HealthDto>("health", Json, ct);
            return body is null ? new HealthStatus(false, "empty health response") : new HealthStatus(body.Ok, body.Provider);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new HealthStatus(false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static ClassificationSource ParseSource(string? source) => source?.ToLowerInvariant() switch
    {
        "cache" => ClassificationSource.Cache,
        "stub" => ClassificationSource.Stub,
        "invoice" => ClassificationSource.Invoice,
        _ => ClassificationSource.Model,
    };

    private sealed record ClassifyRequest(IReadOnlyList<ClassificationInput> Lines);

    private sealed record ClassificationDto(string LineId, string CategoryId, double Confidence, string? Rationale, string? Source);

    private sealed record ClassifyResponse(List<ClassificationDto> Classifications, string? Provider);

    private sealed record SuggestRequest(SupplierNode Node, IReadOnlyList<Alternative> Candidates);

    private sealed record SuggestResponse(List<Suggestion> Suggestions);

    private sealed record HealthDto(bool Ok, string? Provider, int? CacheEntries, bool? ModelConfigured);
}

/// <summary>Used when no AI service is configured or reachable: everything is UNCLASSIFIED, nothing is suggested.</summary>
public sealed class StubAiClient : IAiClient
{
    public string Name => "stub";

    public Task<IReadOnlyList<Classification>> ClassifyAsync(IReadOnlyList<ClassificationInput> lines, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Classification>>(
            lines.Select(l => new Classification(l.LineId, Ids.Unclassified, 0d, "AI not configured", ClassificationSource.Stub)).ToList());

    public Task<IReadOnlyList<Suggestion>> SuggestAsync(SupplierNode node, IReadOnlyList<Alternative> candidates, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Suggestion>>([]);

    public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, "stub"));
}
