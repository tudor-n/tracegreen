using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>The category the buyer sets for a node's dominant line: one of <c>GET /api/categories</c>.</summary>
public sealed record ClassificationRequest(string CategoryId);

/// <summary>A category the buyer may pick (the AI allow-list).</summary>
public sealed record CategoryOption(string Id, string Label);

/// <summary>
/// Buyer corrections of a category (F6). The node is recomputed on the server, the correction is reused for the same
/// description on later imports, and the change is audited as <c>classification.corrected</c> and announced with
/// <c>node.updated</c>. Errors: unknown node → 404 <c>NodeNotFound</c>; a category outside the allow-list → 422
/// <c>CategoryNotAllowed</c>. Sending a correction that is already in place changes nothing and answers 200.
/// </summary>
public static class ClassificationEndpoints
{
    public static IEndpointRouteBuilder MapClassification(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", (ClassificationService classification) =>
                TypedResults.Ok(classification.Categories.Select(c => new CategoryOption(c.Id, c.Label)).ToList()))
            .WithName("ListCategories")
            .WithTags("Classification")
            .WithSummary("The categories a buyer may set for a node, which are also the AI's allow-list");

        app.MapPut("/api/nodes/{nodeId:guid}/classification",
                async (Guid nodeId, ClassificationRequest body, ClassificationService classification, SeedBundle seed, CancellationToken ct) =>
                    (await classification.CorrectAsync(Workspace.Buyer(seed), nodeId, body.CategoryId ?? "", ct)).Match(TypedResults.Ok, ToProblem))
            .WithName("CorrectClassification")
            .WithTags("Classification")
            .WithSummary("Set a node's category: recompute its estimate on the server, keep the correction for that description and publish node.updated")
            .Produces<NodeView>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static IResult ToProblem(ClassificationError error) => Problems.Result(Problems.From(error.Code), error.Message);
}
