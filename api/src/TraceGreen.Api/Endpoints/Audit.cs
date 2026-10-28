using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAudit(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/audit/verify", async (GraphService graph, SeedBundle seed, CancellationToken ct) =>
                TypedResults.Ok(await graph.VerifyAuditAsync(Workspace.Buyer(seed), ct)))
            .WithName("VerifyAuditChain")
            .WithTags("Audit")
            .WithSummary("Recompute every hash of the tamper-evident audit log; brokenAt is the 1-based position of the first bad entry")
            .Produces<ChainVerification>();
        return app;
    }
}
