using System.Globalization;
using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Services;

/// <summary>Maps to HTTP: <see cref="NodeNotFound"/> 404, <see cref="CategoryNotAllowed"/> 422, <see cref="CategoryChanged"/> 409.</summary>
public enum ClassificationErrorCode { NodeNotFound, CategoryNotAllowed, CategoryChanged }

public sealed record ClassificationError(ClassificationErrorCode Code, string Message);

/// <summary>
/// The buyer's classification review (F6). A person confirms or changes a node's category: the category must be on the
/// allow-list (<c>UNCLASSIFIED</c> included), and the node must still have the category the page showed. The node is then
/// rebuilt through <see cref="NodeFactory"/> with the new category, so its sector, spend figure and, once the supplier has
/// answered, its activity figure (the material factor follows the category) are what an import of the same answers would
/// give. The category's source becomes <see cref="ClassificationSource.Buyer"/>, without an AI confidence; the review is
/// <see cref="ClassificationReview.Confirmed"/> when the category stayed and <see cref="ClassificationReview.Corrected"/>
/// when it changed, and a corrected node stays corrected. The write and its <c>classification.reviewed</c> audit entry are
/// atomic; then <c>node.updated</c> with fresh KPIs and <c>audit.appended</c> are published. Nothing personal is recorded:
/// the dashboard has no login, so the actor is the role.
/// </summary>
public sealed class ClassificationService(
    IRepository repo, IEventBus bus, SeedBundle seed, NodeViewMapper mapper, GraphService graph, TimeProvider clock)
{
    public const string AuditAction = "classification.reviewed";

    public async Task<Result<NodeView, ClassificationError>> ReviewAsync(
        Ctx ctx, Guid nodeId, string categoryId, string expectedCategoryId, CancellationToken ct)
    {
        if (!seed.AllowList.Contains(categoryId))
        {
            return Fail(ClassificationErrorCode.CategoryNotAllowed, $"{categoryId} is not a category in this workspace's list.");
        }

        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
        var lane = node is null ? null : seed.Lanes.FirstOrDefault(l => l.Id == node.LaneId);
        if (node is null || invoice is null || lane is null)
        {
            return NotFound(nodeId);
        }

        // Normalised like audit timestamps, so the node's reviewedAt equals the entry's ts after any storage round trip.
        var now = AuditChain.Normalize(clock.GetUtcNow());
        ClassificationWrite Review(SupplierNode current)
        {
            var rebuilt = NodeFactory.NewNode(
                current.Id, invoice, new NodeClassification(categoryId, null, ClassificationSource.Buyer), lane, current.Status,
                current.TransportMode, current.EnergySource, seed, mapper.Factors, current.CreatedAt, current.MassT);
            var review = Outcome(current, categoryId);
            var patch = new ClassificationPatch(
                categoryId, rebuilt.SectorId, ClassificationSource.Buyer, null, review, now, rebuilt.SpendKg, rebuilt.ActivityKg);
            // Strings only (see IngestService): jsonb may re-render numbers and break the chain hash. The entry's ts is the
            // review time; no token, address or person.
            var payload = new JsonObject
            {
                ["nodeId"] = current.Id.ToString("D"),
                ["review"] = Name(review),
                ["fromCategoryId"] = current.CategoryId,
                ["toCategoryId"] = categoryId,
                ["fromSource"] = current.ClassificationSource.ToString().ToLowerInvariant(),
                ["fromConfidence"] = current.AiConfidence?.ToString("R", CultureInfo.InvariantCulture),
            };
            return new ClassificationWrite(patch, prev => AuditChain.Build(prev, ctx.WorkspaceId, now, ctx.Actor, AuditAction, payload));
        }

        var written = await repo.ReviewClassificationAsync(ctx, nodeId, expectedCategoryId, Review, ct);
        switch (written.Status)
        {
            case ReviewClassificationStatus.NodeNotFound:
                return NotFound(nodeId);
            case ReviewClassificationStatus.CategoryChanged:
                return Fail(
                    ClassificationErrorCode.CategoryChanged,
                    $"The category is now {written.Node?.CategoryId ?? "unknown"}, not {expectedCategoryId}; reload and review again.");
        }
        if (written is not { Node: { } reviewed, Entry: { } entry })
        {
            throw new InvalidOperationException("The repository reported a review without the node or its audit entry.");
        }

        var view = mapper.Map(reviewed, invoice);
        var kpis = await graph.KpisAsync(ctx, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new NodeUpdated(seq, ts, ctx.WorkspaceId, view, kpis));
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, ctx.WorkspaceId, entry));
        return Result<NodeView, ClassificationError>.Ok(view);
    }

    /// <summary>Kept the category: confirmed, unless a person already changed it once, which no later review undoes.</summary>
    public static ClassificationReview Outcome(SupplierNode current, string categoryId) =>
        current.Review == ClassificationReview.Corrected || !string.Equals(current.CategoryId, categoryId, StringComparison.Ordinal)
            ? ClassificationReview.Corrected
            : ClassificationReview.Confirmed;

    private static string Name(ClassificationReview review) => review switch
    {
        ClassificationReview.Confirmed => "confirmed",
        ClassificationReview.Corrected => "corrected",
        _ => throw new ArgumentOutOfRangeException(nameof(review), review, null),
    };

    private static Result<NodeView, ClassificationError> NotFound(Guid nodeId) =>
        Fail(ClassificationErrorCode.NodeNotFound, $"No node {nodeId} on the graph.");

    private static Result<NodeView, ClassificationError> Fail(ClassificationErrorCode code, string message) =>
        Result<NodeView, ClassificationError>.Fail(new ClassificationError(code, message));
}
