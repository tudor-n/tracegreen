using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Domain.Services;

/// <summary>Builds the dashboard snapshot and the node views and KPIs that events carry.</summary>
public sealed class GraphService(IRepository repo, IEventBus bus, NodeViewMapper mapper)
{
    private const int AuditPage = 500;

    public async Task<GraphSnapshot> SnapshotAsync(Ctx ctx, CancellationToken ct)
    {
        // Read the sequence first: anything published after this point is newer than the snapshot and gets applied on top.
        var lastSeq = bus.LastSeq(ctx.WorkspaceId);
        var buyer = await repo.GetBuyerAsync(ctx, ct);
        var nodes = await repo.ListNodesAsync(ctx, ct);
        var views = new List<NodeView>(nodes.Count);
        foreach (var node in nodes)
        {
            views.Add(await ViewAsync(ctx, node, ct));
        }
        var audit = await AuditAsync(ctx, ct);
        return new GraphSnapshot(buyer, views, KpiCalculator.Compute(nodes), audit, lastSeq, repo.Mode);
    }

    public async Task<NodeView> ViewAsync(Ctx ctx, SupplierNode node, CancellationToken ct)
    {
        var invoice = await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct)
            ?? throw new InvalidOperationException($"Node {node.Id} has no invoice {node.InvoiceId}.");
        return mapper.Map(node, invoice);
    }

    public async Task<Kpis> KpisAsync(Ctx ctx, CancellationToken ct) =>
        KpiCalculator.Compute(await repo.ListNodesAsync(ctx, ct));

    private async Task<IReadOnlyList<AuditEntry>> AuditAsync(Ctx ctx, CancellationToken ct)
    {
        var all = new List<AuditEntry>();
        while (true)
        {
            var page = await repo.ListAuditAsync(ctx, all.Count == 0 ? 0 : all[^1].Id, AuditPage, ct);
            all.AddRange(page);
            if (page.Count < AuditPage)
            {
                return all;
            }
        }
    }
}
