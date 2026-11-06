using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// AI-suggested alternatives (F11): which of a node's curated alternatives the AI picks, each with a short reason. Never
/// a new supplier and never a figure; the simulator computes those. Shown as "AI-suggested · illustrative". A slow or
/// absent AI answers an empty list with <c>provider</c> "stub". Errors: unknown node → 404 <c>NodeNotFound</c>.
/// </summary>
public static class SuggestionEndpoints
{
    public static IEndpointRouteBuilder MapSuggestions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/nodes/{nodeId:guid}/suggestions", async (Guid nodeId, SuggestionService suggestions, SeedBundle seed, CancellationToken ct) =>
                await suggestions.SuggestAsync(Workspace.Buyer(seed), nodeId, ct) is { } view
                    ? Results.Ok(view)
                    : Problems.Result(ProblemCode.NodeNotFound, $"Node {nodeId} not found."))
            .WithName("SuggestAlternatives")
            .WithTags("Scenarios")
            .WithSummary("Which of the node's curated alternatives the AI picks, with a short reason each; no figures")
            .Produces<SuggestionsView>()
            .ProducesApiProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
