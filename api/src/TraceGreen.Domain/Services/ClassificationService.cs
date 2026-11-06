using System.Globalization;
using System.Text.Json.Nodes;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>Maps to HTTP: <see cref="NodeNotFound"/> 404, <see cref="CategoryNotAllowed"/> 422.</summary>
public enum ClassificationErrorCode { NodeNotFound, CategoryNotAllowed }

public sealed record ClassificationError(ClassificationErrorCode Code, string Message);

/// <summary>
/// The buyer's correction of a node's category (F6). The node is rebuilt through <see cref="NodeFactory"/> with the new
/// category and everything else it had (status, the supplier's answers, a confirmed mass), so its figures are what an
/// import with that category and the same answers would give; carbon stays server-side. The correction is kept by the
/// dominant line's description key and wins on every later import of that description. Audited as
/// <c>classification.corrected</c> and announced with <c>node.updated</c> carrying fresh KPIs.
/// </summary>
public sealed class ClassificationService(IRepository repo, IEventBus bus, SeedBundle seed, NodeViewMapper mapper, GraphService graph, TimeProvider clock)
{
    /// <summary>The categories a buyer may pick: the AI allow-list, in seed order.</summary>
    public IReadOnlyList<Category> Categories => seed.Categories;

    public async Task<Result<NodeView, ClassificationError>> CorrectAsync(Ctx ctx, Guid nodeId, string categoryId, CancellationToken ct)
    {
        if (!seed.AllowList.Contains(categoryId))
        {
            return Fail(ClassificationErrorCode.CategoryNotAllowed, $"Category {categoryId} is not one of the allowed categories.");
        }
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
        if (node is null || invoice is null)
        {
            return Fail(ClassificationErrorCode.NodeNotFound, $"Node {nodeId} not found.");
        }
        var lane = seed.Lanes.Single(l => l.Id == node.LaneId);
        var cacheKey = CacheKey.Compute(InvoiceFacts.DominantLine(invoice.Extracted.Lines).Description);
        var now = clock.GetUtcNow();
        var classification = new NodeClassification(categoryId, 1d, ClassificationSource.Correction, LineClassification.CorrectionRationale);

        var result = await repo.RecordCorrectionAsync(ctx, nodeId, current =>
        {
            if (current.CategoryId == categoryId && current.ClassificationSource == ClassificationSource.Correction)
            {
                return null;
            }
            var rebuilt = NodeFactory.NewNode(
                current.Id, invoice, classification, lane, current.Status, current.TransportMode, current.EnergySource, seed,
                mapper.Factors, current.CreatedAt, current.MassT);
            // Strings only (see IngestService): jsonb may re-render decimals and break the chain hash.
            var payload = new JsonObject
            {
                ["nodeId"] = current.Id.ToString("D"),
                ["cacheKey"] = cacheKey,
                ["from"] = current.CategoryId,
                ["to"] = categoryId,
                ["previousSource"] = current.ClassificationSource.ToString().ToLowerInvariant(),
                ["status"] = current.Status.ToString().ToUpperInvariant(),
                ["carbonKgBefore"] = current.CarbonKg.ToString(CultureInfo.InvariantCulture),
                ["carbonKgAfter"] = rebuilt.CarbonKg.ToString(CultureInfo.InvariantCulture),
            };
            return new CorrectionPlan(
                rebuilt,
                new ClassificationCorrection(ctx.WorkspaceId, cacheKey, categoryId, current.Id, ctx.Actor, now),
                prev => AuditChain.Build(prev, ctx.WorkspaceId, now, ctx.Actor, "classification.corrected", payload));
        }, ct);

        switch (result.Status)
        {
            case RecordCorrectionStatus.NodeNotFound:
                return Fail(ClassificationErrorCode.NodeNotFound, $"Node {nodeId} not found.");
            case RecordCorrectionStatus.Unchanged:
                return Result<NodeView, ClassificationError>.Ok(mapper.Map(result.Node!, invoice));
        }

        var view = mapper.Map(result.Node!, invoice);
        var kpis = await graph.KpisAsync(ctx, ct);
        var entry = result.Entry!;
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new NodeUpdated(seq, ts, ctx.WorkspaceId, view, kpis));
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, ctx.WorkspaceId, entry));
        return Result<NodeView, ClassificationError>.Ok(view);
    }

    private static Result<NodeView, ClassificationError> Fail(ClassificationErrorCode code, string message) =>
        Result<NodeView, ClassificationError>.Fail(new ClassificationError(code, message));
}
