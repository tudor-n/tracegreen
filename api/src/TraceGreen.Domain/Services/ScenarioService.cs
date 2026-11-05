using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Maps to HTTP: <see cref="NodeNotFound"/> and <see cref="AlternativeNotFound"/> 404; <see cref="InvalidChange"/>,
/// <see cref="ModeNotAvailable"/> and <see cref="MassUnknown"/> 422.
/// </summary>
public enum ScenarioErrorCode { NodeNotFound, AlternativeNotFound, InvalidChange, ModeNotAvailable, MassUnknown }

public sealed record ScenarioError(ScenarioErrorCode Code, string Message);

/// <summary>
/// The buyer's what-if (F4). Resolves the node's lane, category and factors, or the alternative's, and hands them to
/// <see cref="Simulator"/>; every number comes from there and is compared against the node as it is now (its activity
/// figure once verified, its spend estimate before). The base node is never touched. A saved scenario is recomputed
/// here, never taken from the client, and announced with <c>scenario.saved</c>.
/// </summary>
public sealed class ScenarioService(IRepository repo, IEventBus bus, SeedBundle seed, NodeViewMapper mapper, TimeProvider clock, ServiceOptions options)
{
    public async Task<Result<ScenarioResult, ScenarioError>> SimulateAsync(Ctx ctx, Guid nodeId, ScenarioChange change, CancellationToken ct) =>
        (await RunAsync(ctx, nodeId, change, ct)).Match(
            run => Result<ScenarioResult, ScenarioError>.Ok(run.Result),
            Result<ScenarioResult, ScenarioError>.Fail);

    public async Task<Result<Scenario, ScenarioError>> SaveAsync(Ctx ctx, Guid nodeId, ScenarioChange change, CancellationToken ct)
    {
        var run = await RunAsync(ctx, nodeId, change, ct);
        if (!run.IsOk)
        {
            return Result<Scenario, ScenarioError>.Fail(run.Error);
        }

        var (kind, normalised, result) = run.Value;
        var scenario = new Scenario(Ids.NewId(), ctx.WorkspaceId, nodeId, kind, normalised, result, clock.GetUtcNow());
        await repo.SaveScenarioAsync(ctx, scenario, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new ScenarioSaved(seq, ts, ctx.WorkspaceId, scenario));
        return Result<Scenario, ScenarioError>.Ok(scenario);
    }

    /// <summary>The alternatives that supply the node's category: the supplier swaps the simulator offers for it.</summary>
    public static IEnumerable<Alternative> AlternativesFor(SeedBundle seed, string categoryId) =>
        seed.Alternatives.Where(a => string.Equals(a.NodeCategory, categoryId, StringComparison.Ordinal));

    private sealed record Run(ScenarioKind Kind, ScenarioChange Change, ScenarioResult Result);

    private async Task<Result<Run, ScenarioError>> RunAsync(Ctx ctx, Guid nodeId, ScenarioChange change, CancellationToken ct)
    {
        var alternativeId = string.IsNullOrWhiteSpace(change.AlternativeId) ? null : change.AlternativeId;
        if ((change.Mode is null) == (alternativeId is null))
        {
            return Fail(ScenarioErrorCode.InvalidChange, "Give either a transport mode or an alternative supplier, not both.");
        }

        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        if (node is null)
        {
            return Fail(ScenarioErrorCode.NodeNotFound, $"No node {nodeId} in this workspace.");
        }

        return change.Mode is { } mode ? ModeChange(node, mode) : SupplierSwap(node, alternativeId!);
    }

    private Result<Run, ScenarioError> ModeChange(SupplierNode node, TransportMode mode)
    {
        var lane = LaneOf(node.LaneId);
        if (!Simulator.AvailableModes(lane).Contains(mode))
        {
            return Fail(ScenarioErrorCode.ModeNotAvailable, $"Lane {lane.Id} has no {Name(mode)} leg.");
        }
        // Without a mass the activity figure reads 0 kg, which would show as a large saving; refuse rather than show it.
        if (node.MassT is null)
        {
            return Fail(ScenarioErrorCode.MassUnknown, "The invoice gives no mass for this item, so a change of mode cannot be estimated.");
        }

        var material = seed.CategoryOf(node.CategoryId).MaterialKey;
        var result = Simulator.ModeChange(node, lane, material, mode, mapper.Factors, options.EtsPriceEurPerT);
        return Result<Run, ScenarioError>.Ok(new Run(ScenarioKind.Mode, new ScenarioChange(mode, null), result));
    }

    private Result<Run, ScenarioError> SupplierSwap(SupplierNode node, string alternativeId)
    {
        var alternative = seed.Alternatives.FirstOrDefault(a => string.Equals(a.Id, alternativeId, StringComparison.Ordinal));
        if (alternative is null)
        {
            return Fail(ScenarioErrorCode.AlternativeNotFound, $"No alternative supplier {alternativeId}.");
        }
        if (!string.Equals(alternative.NodeCategory, node.CategoryId, StringComparison.Ordinal))
        {
            return Fail(ScenarioErrorCode.InvalidChange, $"{alternative.Label} does not supply this node's category.");
        }

        var material = seed.CategoryOf(alternative.NodeCategory).MaterialKey;
        var result = Simulator.SupplierSwap(node, alternative, LaneOf(alternative.LaneId), material, mapper.Factors, options.EtsPriceEurPerT);
        return Result<Run, ScenarioError>.Ok(new Run(ScenarioKind.Supplier, new ScenarioChange(null, alternative.Id), result));
    }

    // SeedLoader validates every node and alternative lane, so a miss here is a broken seed, not a bad request.
    private Lane LaneOf(string laneId) =>
        seed.Lanes.FirstOrDefault(l => l.Id == laneId) ?? throw new InvalidOperationException($"Unknown lane {laneId}.");

    private static string Name(TransportMode mode) => mode.ToString().ToLowerInvariant();

    private static Result<Run, ScenarioError> Fail(ScenarioErrorCode code, string message) =>
        Result<Run, ScenarioError>.Fail(new ScenarioError(code, message));
}
