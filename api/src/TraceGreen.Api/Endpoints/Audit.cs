using TraceGreen.Domain.Audit;

namespace TraceGreen.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAudit(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/audit/verify", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("VerifyAuditChain")
            .WithTags("Audit")
            .WithSummary("Recompute every hash of the tamper-evident audit log; brokenAt is the 1-based position of the first bad entry")
            .Produces<ChainVerification>();
        return app;
    }
}
