using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// One change for <see cref="NodeId"/>: either a transport <see cref="Mode"/> on the node's lane, or an
/// <see cref="AlternativeId"/> from the node's <c>alternatives</c>. Leave the other field out (or null).
/// </summary>
public sealed record ScenarioRequest(Guid NodeId, TransportMode? Mode = null, string? AlternativeId = null);

/// <summary>
/// The simulator (F4). Both routes compute the same <see cref="ScenarioResult"/> on the server against the node as it is
/// now and never change the node; cost, lead time and the MAC rest on illustrative lane figures (<c>illustrative</c>).
/// Errors: unknown node → 404 <c>NodeNotFound</c>; unknown alternative → 404 <c>AlternativeNotFound</c>; neither or both
/// fields, or an alternative of another category → 422 <c>InvalidChange</c>; a mode the lane lacks → 422
/// <c>ModeNotAvailable</c>; a mode change on a node without a mass → 422 <c>MassUnknown</c>.
/// </summary>
public static class ScenarioEndpoints
{
    public static IEndpointRouteBuilder MapScenarios(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/simulate", async (ScenarioRequest body, ScenarioService scenarios, SeedBundle seed, CancellationToken ct) =>
                (await scenarios.SimulateAsync(Workspace.Buyer(seed), body.NodeId, Change(body), ct)).Match(TypedResults.Ok, ToProblem))
            .WithName("Simulate")
            .WithTags("Scenarios")
            .WithSummary("What a change of mode or supplier would do to a node's carbon, cost and lead time, with the MAC and its badge; nothing is stored")
            .Produces<ScenarioResult>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/scenarios", async (ScenarioRequest body, ScenarioService scenarios, SeedBundle seed, CancellationToken ct) =>
                (await scenarios.SaveAsync(Workspace.Buyer(seed), body.NodeId, Change(body), ct))
                    .Match(scenario => TypedResults.Created((string?)null, scenario), ToProblem))
            .WithName("SaveScenario")
            .WithTags("Scenarios")
            .WithSummary("Recompute a change on the server, save it next to the node (never over it) and publish scenario.saved")
            .Produces<Scenario>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static ScenarioChange Change(ScenarioRequest body) => new(body.Mode, body.AlternativeId);

    private static IResult ToProblem(ScenarioError error) => Problems.Result(Problems.From(error.Code), error.Message);
}
