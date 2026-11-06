using TraceGreen.Domain.Seed;

namespace TraceGreen.Api.Endpoints;

/// <summary>A category a buyer may give a node: its id for <c>categoryId</c>, and its label.</summary>
public sealed record CategoryOption(string Id, string Label);

/// <summary>
/// The classification review's choices (F6): every category in <c>seed/categories.json</c>, in its order, which is also
/// the AI's allow-list; <c>UNCLASSIFIED</c> included. The dashboard offers exactly these, so it never sends a category the
/// review route refuses with 422.
/// </summary>
public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategories(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", (SeedBundle seed) =>
                TypedResults.Ok<IReadOnlyList<CategoryOption>>(seed.Categories.Select(c => new CategoryOption(c.Id, c.Label)).ToList()))
            .WithName("ListCategories")
            .WithTags("Graph")
            .WithSummary("The categories a buyer may set for a node, which are also the AI's allow-list");
        return app;
    }
}
