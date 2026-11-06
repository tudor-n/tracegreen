using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// A person's decision on a node's category. <see cref="CategoryId"/> is an id from <c>seed/categories.json</c>
/// (<c>UNCLASSIFIED</c> included); sending the node's current category confirms it. <see cref="ExpectedCategoryId"/> is
/// the category the page showed, so a decision made on a stale page is refused instead of overwriting a newer one.
/// </summary>
public sealed record ClassificationReviewRequest(string CategoryId, string ExpectedCategoryId);

/// <summary>
/// The classification review (F6). Every AI-mapped node waits with <c>review: pending</c> until a person confirms or
/// changes its category here; the answer is the node as it now is, with its figures recomputed for the category. Like the
/// rest of the dashboard the route has no authentication, so the audit entry names the role ("buyer"), never a person.
/// Errors: a category outside the allow-list → 422 <c>CategoryNotAllowed</c>; unknown node → 404 <c>NodeNotFound</c>;
/// <c>expectedCategoryId</c> no longer the node's category → 409 <c>CategoryChanged</c>.
/// </summary>
public static class ClassificationEndpoints
{
    public static IEndpointRouteBuilder MapClassification(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/nodes/{nodeId:guid}/classification",
                async (Guid nodeId, ClassificationReviewRequest body, ClassificationService classification, SeedBundle seed, CancellationToken ct) =>
                    (await classification.ReviewAsync(Workspace.Buyer(seed), nodeId, body.CategoryId, body.ExpectedCategoryId, ct))
                        .Match(TypedResults.Ok, error => ToProblem(error, nodeId)))
            .WithName("ReviewClassification")
            .WithTags("Graph")
            .WithSummary("Confirm or change a node's category; recomputes its figures, appends classification.reviewed and publishes node.updated")
            .Produces<NodeView>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static IResult ToProblem(ClassificationError error, Guid nodeId) => error.Code switch
    {
        ClassificationErrorCode.NodeNotFound => Problems.Result(ProblemCode.NodeNotFound, error.Message, nodeId),
        ClassificationErrorCode.CategoryNotAllowed => Problems.Result(ProblemCode.CategoryNotAllowed, error.Message),
        ClassificationErrorCode.CategoryChanged => Problems.Result(ProblemCode.CategoryChanged, error.Message, nodeId),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, null),
    };
}
