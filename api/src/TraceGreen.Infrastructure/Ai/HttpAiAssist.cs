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

    public async Task<ExtractOutcome> ExtractPdfAsync(byte[] pdf, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("extract-pdf", new ExtractRequest(Convert.ToBase64String(pdf)), Json, ct);
        if ((int)response.StatusCode is 422 or 503)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
            return ExtractOutcome.Failed(problem?.Detail?.Code switch
            {
                "no_text" => ExtractFailure.NoText,
                "too_large" => ExtractFailure.TooLarge,
                "unreadable" => ExtractFailure.Unreadable,
                _ => ExtractFailure.Unavailable,
            });
        }
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ExtractResponse>(Json, ct)
            ?? throw new InvalidOperationException("Empty response from the AI service.");
        return new ExtractOutcome(body.Invoice, body.Found ?? new Dictionary<string, bool>(), body.Provider ?? "stub", body.Pages, null);
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

    private sealed record ExtractRequest(string PdfBase64);

    private sealed record ExtractResponse(DraftInvoice Invoice, Dictionary<string, bool>? Found, string? Provider, int Pages);

    private sealed record ProblemDetail(string? Code, string? Message);

    private sealed record ProblemBody(ProblemDetail? Detail);
}

/// <summary>No AI service: the chat answers from its templates only, and no PDF can be read.</summary>
public sealed class StubAiAssist : IAiAssist
{
    public Task<ChatOutcome> ChatAsync(ChatInput input, CancellationToken ct) =>
        Task.FromResult(new ChatOutcome(false, "", [], "stub"));

    public Task<ExtractOutcome> ExtractPdfAsync(byte[] pdf, CancellationToken ct) =>
        Task.FromResult(ExtractOutcome.Failed(ExtractFailure.Unavailable));

    public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, "stub"));
}
