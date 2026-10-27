using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>The answers a phone would send for <see cref="NodeId"/>; leave a question's field out (or null) to skip it.</summary>
public sealed record SimulateAnswerRequest(Guid NodeId, TransportMode? TransportMode = null, EnergySource? EnergySource = null, decimal? MassT = null);

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

        app.MapPost("/api/dev/simulate-answer", async (SimulateAnswerRequest body, HttpContext http, AppConfig config, DemoService demo, SeedBundle seed, CancellationToken ct) =>
            {
                if (!config.DemoTools)
                {
                    return Results.NotFound();
                }
                var result = await demo.SimulateAnswerAsync(
                    Workspace.Demo(seed), body.NodeId, body.TransportMode, body.EnergySource, body.MassT, VerifyEndpoints.ClientAddress(http), ct);
                return result.Match(TypedResults.Ok, VerifyEndpoints.ToProblem);
            })
            .WithName("SimulateAnswer")
            .WithTags("Dev")
            .WithSummary("Answer for a supplier without a phone: mints a link and submits through the same path, so audit rows and events match")
            .Produces<VerifyOutcome>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);
        return app;
    }
}
