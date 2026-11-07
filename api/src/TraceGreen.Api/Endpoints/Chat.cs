using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// The buyer's question and up to three earlier questions (the panel sends questions only, never answers). <c>model</c> is
/// one of <c>GET /api/chat/models</c>'s ids, which then answers alone; left out or null, the chat's models answer in order.
/// </summary>
public sealed record ChatRequest(string? Question, IReadOnlyList<string>? History = null, string? Model = null);

/// <summary>
/// The data chat (F13). The AI writes the sentence but cites every value and name by fact id; the API checks the
/// answer and fills the values in from its own data, so no figure in an answer comes from a model. A question the data
/// does not answer comes back <c>notAnswered</c>, and <c>unavailable</c> when no AI answered. Read-only, rate limited
/// per address. Errors: an empty or over-long question → 422 <c>InvalidQuestion</c>; a model the chat does not offer → 422
/// <c>UnknownModel</c>, one without its key → 422 <c>ModelNotReady</c>; too many questions → 429.
/// </summary>
public static class ChatEndpoints
{
    public const string RateLimitPolicy = "chat";

    /// <summary>The chat's allowance is a multiple of <c>RATE_LIMIT_PER_MIN</c>: a buyer asks several questions in a row.</summary>
    public const int RateLimitFactor = 3;

    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (ChatRequest body, ChatService chat, SeedBundle seed, ILogger<ChatService> log, CancellationToken ct) =>
                (await chat.AskAsync(Workspace.Buyer(seed), body.Question, body.History, body.Model, ct)).Match(
                    result =>
                    {
                        if (result.LogNote is { } note)
                        {
                            log.LogInformation("Chat answer checks: {Note}", note);
                        }
                        return Results.Ok(result.Answer);
                    },
                    error => Problems.Result(Problems.From(error.Code), error.Message)))
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("Chat")
            .WithTags("Chat")
            .WithSummary("Answer a question about the workspace's data; every value in the answer is filled in from the data, never by the AI")
            .Produces<ChatAnswer>()
            .ProducesApiProblem(StatusCodes.Status422UnprocessableEntity, StatusCodes.Status429TooManyRequests);

        app.MapGet("/api/chat/models", async (ChatService chat, CancellationToken ct) => Results.Ok(await chat.ModelsAsync(ct)))
            .WithName("ChatModels")
            .WithTags("Chat")
            .WithSummary("The AI models the chat can be asked with, in the order it tries them; a model without its key is listed as not ready, with the reason")
            .Produces<ChatModels>();
        return app;
    }
}
