using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Demo controls: import the demo invoice with its fixed ids, and go back to the seed start state
/// (N2 verified, N3 and N4 estimated, N5 partial, no N1).
/// </summary>
public sealed class DemoService(IRepository repo, IEventBus bus, SeedBundle seed, IngestService ingest)
{
    /// <summary>The seed node the demo imports live; it is the one <c>demo-state.json</c> leaves out of the start state.</summary>
    public const string DemoNodeKey = "N1";

    public async Task<Result<IngestResult, IngestError>> IngestDemoAsync(Ctx ctx, CancellationToken ct)
    {
        var spec = seed.DemoState.Nodes.Single(n => n.Key == DemoNodeKey);
        var xml = await File.ReadAllTextAsync(seed.FilePath(spec.InvoiceFile), ct);
        return await ingest.IngestAsync(ctx, xml, new IngestPins(spec.NodeId, spec.InvoiceId, spec.LaneId), ct);
    }

    public async Task ResetAsync(Ctx ctx, CancellationToken ct)
    {
        await repo.SeedAsync(seed, reset: true, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new GraphReset(seq, ts, ctx.WorkspaceId));
    }
}
