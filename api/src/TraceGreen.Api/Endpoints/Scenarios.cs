using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// One change to a node: a transport <see cref="Mode"/> or a swap to the alternative supplier <see cref="AlternativeId"/>,
/// exactly one of the two. The shape of a scenario's <c>change</c> with the node added, so the SPA sends
/// <c>{ nodeId, ...change }</c> to both routes.
/// </summary>
public sealed record ScenarioRequest(Guid NodeId, TransportMode? Mode = null, string? AlternativeId = null);

/// <summary>
/// The simulator (F4). Every figure is computed here against the node as it is stored, and the node itself never
/// changes. Cost, lead time and the MAC rest on the illustrative lane table, so every result says
/// <c>illustrative: true</c>. Errors: unknown node → 404 <c>NodeNotFound</c>; unknown alternative → 404
/// <c>AlternativeNotFound</c>; a mode the lane has no leg for, an alternative of another category, or not exactly one
/// change → 422 <c>InvalidChange</c>.
/// </summary>
public static class ScenarioEndpoints
{
    public static IEndpointRouteBuilder MapScenarios(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/nodes/{nodeId:guid}/alternatives", (Guid nodeId) => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("ListAlternatives")
            .WithTags("Scenarios")
            .WithSummary("The alternative suppliers a node can be swapped to: those whose category is the node's; empty when none fits")
            .Produces<IReadOnlyList<AlternativeView>>()
            .ProducesApiProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/simulate", (ScenarioRequest body) => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("Simulate")
            .WithTags("Scenarios")
            .WithSummary("What one change would do to a node: carbon, cost and lead time with deltas, the MAC and its badge; nothing is stored")
            .Produces<ScenarioResult>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/scenarios", (ScenarioRequest body) => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("SaveScenario")
            .WithTags("Scenarios")
            .WithSummary("Compute the change as /api/simulate does, store it as a scenario and publish scenario.saved; the node is unchanged")
            .Produces<Scenario>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);
        return app;
    }
}
