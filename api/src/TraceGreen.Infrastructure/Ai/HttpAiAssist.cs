using System.Net.Http.Json;
using System.Text.Json;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Infrastructure.Ai;

/// <summary>HTTP adapter for the AI service's longer tasks (ai/app/schemas.py is the contract). Throws on transport errors.</summary>
public sealed class HttpAiAssist(HttpClient http) : IAiAssist
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ChatOutcome> ChatAsync(ChatInput input, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("chat", input, Json, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, ct)
            ?? throw new InvalidOperationException("Empty response from the AI service.");
        return new ChatOutcome(body.Answerable, body.Text ?? "", body.FactIds ?? [], body.Provider ?? "stub");
    }

    public async Task<HealthStatus> HealthAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("health", ct);
            return new HealthStatus(response.IsSuccessStatusCode, ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new HealthStatus(false, ex.GetType().Name);
        }
    }

    private sealed record ChatResponse(bool Answerable, string? Text, List<string>? FactIds, string? Provider);
}

/// <summary>No AI service: the chat answers from its templates only.</summary>
public sealed class StubAiAssist : IAiAssist
{
    public Task<ChatOutcome> ChatAsync(ChatInput input, CancellationToken ct) =>
        Task.FromResult(new ChatOutcome(false, "", [], "stub"));

    public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, "stub"));
}
