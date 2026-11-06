using TraceGreen.Domain.Model;

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
/// Errors: unknown node → 404 <c>NodeNotFound</c>; a category outside the allow-list → 422 <c>CategoryNotAllowed</c>;
/// <c>expectedCategoryId</c> no longer the node's category → 409 <c>CategoryChanged</c>.
/// </summary>
public static class ClassificationEndpoints
{
    public static IEndpointRouteBuilder MapClassification(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/nodes/{nodeId:guid}/classification", (Guid nodeId, ClassificationReviewRequest body) =>
                // Contract stage: the route and its shapes are final; the review itself lands in the next change.
                Results.Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Classification review not implemented yet"))
            .WithName("ReviewClassification")
            .WithTags("Graph")
            .WithSummary("Confirm or change a node's category; recomputes its figures, appends classification.reviewed and publishes node.updated")
            .Produces<NodeView>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);
        return app;
    }
}
