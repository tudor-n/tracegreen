using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>Demo tools. Always described in OpenAPI so the generated client does not depend on the environment; 404 unless <c>DEMO_TOOLS=1</c>.</summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDev(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/reset", async (AppConfig config, DemoService demo, SeedBundle seed, CancellationToken ct) =>
            {
                if (!config.DemoTools)
                {
                    return Results.NotFound();
                }
                await demo.ResetAsync(Workspace.Demo(seed), ct);
                return Results.NoContent();
            })
            .WithName("ResetDemo")
            .WithTags("Dev")
            .WithSummary("Back to the seed start state, then publish graph.reset")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }
}
