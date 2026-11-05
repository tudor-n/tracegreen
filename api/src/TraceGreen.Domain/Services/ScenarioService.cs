using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Mapped to HTTP by the scenario endpoints: <see cref="NodeNotFound"/> and <see cref="AlternativeNotFound"/> 404,
/// <see cref="InvalidChange"/> 422.
/// </summary>
public enum ScenarioErrorCode { NodeNotFound, AlternativeNotFound, InvalidChange }

public sealed record ScenarioError(ScenarioErrorCode Code, string Message);

/// <summary>
/// The simulator's use cases (F4). The carbon math is <see cref="Simulator"/>'s; this service only resolves its inputs
/// (the node's lane and material, the alternative with its own lane and material, the factor table and the carbon price)
/// and checks the change: exactly one of a mode the lane has a leg for, or an alternative of the node's category. A
/// simulation stores nothing. A save stores the scenario and publishes <c>scenario.saved</c>. Neither touches the node.
/// </summary>
public sealed class ScenarioService(
    IRepository repo,
    IEventBus bus,
    SeedBundle seed,
    NodeViewMapper mapper,
    TimeProvider clock,
    ServiceOptions options)
{
    public async Task<Result<IReadOnlyList<AlternativeView>, ScenarioError>> AlternativesAsync(Ctx ctx, Guid nodeId, CancellationToken ct)
    {
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        if (node is null)
        {
            return Result<IReadOnlyList<AlternativeView>, ScenarioError>.Fail(NodeMissing(nodeId));
        }
        return Result<IReadOnlyList<AlternativeView>, ScenarioError>.Ok(
            seed.Alternatives.Where(a => a.NodeCategory == node.CategoryId).Select(a => new AlternativeView(a.Id, a.Label, a.Country)).ToList());
    }

    public async Task<Result<ScenarioResult, ScenarioError>> SimulateAsync(Ctx ctx, Guid nodeId, ScenarioChange change, CancellationToken ct)
    {
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        return node is null ? Result<ScenarioResult, ScenarioError>.Fail(NodeMissing(nodeId)) : Compute(node, change);
    }

    public async Task<Result<Scenario, ScenarioError>> SaveAsync(Ctx ctx, Guid nodeId, ScenarioChange change, CancellationToken ct)
    {
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        if (node is null)
        {
            return Result<Scenario, ScenarioError>.Fail(NodeMissing(nodeId));
        }
        var computed = Compute(node, change);
        if (!computed.IsOk)
        {
            return Result<Scenario, ScenarioError>.Fail(computed.Error);
        }

        var kind = change.Mode is null ? ScenarioKind.Supplier : ScenarioKind.Mode;
        var scenario = new Scenario(Ids.NewId(), ctx.WorkspaceId, node.Id, kind, change, computed.Value, clock.GetUtcNow());
        await repo.SaveScenarioAsync(ctx, scenario, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new ScenarioSaved(seq, ts, ctx.WorkspaceId, scenario));
        return Result<Scenario, ScenarioError>.Ok(scenario);
    }

    private Result<ScenarioResult, ScenarioError> Compute(SupplierNode node, ScenarioChange change)
    {
        switch (change)
        {
            case { Mode: { } mode, AlternativeId: null }:
            {
                var lane = LaneOf(node.LaneId);
                if (!Simulator.AvailableModes(lane).Contains(mode))
                {
                    return Invalid($"Lane {lane.Id} has no {mode.ToString().ToLowerInvariant()} leg.");
                }
                var material = seed.CategoryOf(node.CategoryId).MaterialKey;
                return Result<ScenarioResult, ScenarioError>.Ok(
                    Simulator.ModeChange(node, lane, material, mode, mapper.Factors, options.EtsPriceEurPerT));
            }
            case { Mode: null, AlternativeId: { } alternativeId }:
            {
                var alternative = seed.Alternatives.FirstOrDefault(a => a.Id == alternativeId);
                if (alternative is null)
                {
                    return Result<ScenarioResult, ScenarioError>.Fail(
                        new ScenarioError(ScenarioErrorCode.AlternativeNotFound, $"No alternative supplier {alternativeId}."));
                }
                if (alternative.NodeCategory != node.CategoryId)
                {
                    return Invalid($"Alternative {alternative.Id} supplies {alternative.NodeCategory}, not {node.CategoryId}.");
                }
                var material = seed.CategoryOf(alternative.NodeCategory).MaterialKey;
                return Result<ScenarioResult, ScenarioError>.Ok(
                    Simulator.SupplierSwap(node, alternative, LaneOf(alternative.LaneId), material, mapper.Factors, options.EtsPriceEurPerT));
            }
            default:
                return Invalid("Give either a mode or an alternative, not both and not neither.");
        }
    }

    /// <summary>
    /// The seed loader checks the lanes of the demo nodes and the alternatives, and ingest picks a node's lane from the
    /// same table, so a miss is a data error rather than a bad request.
    /// </summary>
    private Lane LaneOf(string laneId) =>
        seed.Lanes.FirstOrDefault(l => l.Id == laneId) ?? throw new InvalidOperationException($"Unknown lane {laneId}.");

    private static ScenarioError NodeMissing(Guid nodeId) => new(ScenarioErrorCode.NodeNotFound, $"No node {nodeId} on the graph.");

    private static Result<ScenarioResult, ScenarioError> Invalid(string message) =>
        Result<ScenarioResult, ScenarioError>.Fail(new ScenarioError(ScenarioErrorCode.InvalidChange, message));
}
