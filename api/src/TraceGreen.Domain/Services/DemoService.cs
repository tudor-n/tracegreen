using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Services;

/// <summary>Demo controls: back to the seed start state (N2 verified, N3 and N4 estimated, N5 partial, no N1).</summary>
public sealed class DemoService(IRepository repo, IEventBus bus, SeedBundle seed)
{
    public async Task ResetAsync(Ctx ctx, CancellationToken ct)
    {
        await repo.SeedAsync(seed, reset: true, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new GraphReset(seq, ts, ctx.WorkspaceId));
    }
}
