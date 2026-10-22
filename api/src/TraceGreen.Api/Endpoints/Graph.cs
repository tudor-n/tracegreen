using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

public static class GraphEndpoints
{
    public static IEndpointRouteBuilder MapGraph(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/graph", async (GraphService graph, SeedBundle seed, CancellationToken ct) =>
                TypedResults.Ok(await graph.SnapshotAsync(Workspace.Buyer(seed), ct)))
            .WithName("GetGraph")
            .WithTags("Graph")
            .WithSummary("Buyer, nodes as rendered, KPIs, audit entries and the event sequence the snapshot is current to")
            .Produces<GraphSnapshot>();
        return app;
    }
}
