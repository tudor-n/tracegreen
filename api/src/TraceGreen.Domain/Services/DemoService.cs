using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Demo controls: import the demo invoice with its fixed ids, and go back to the seed start state
/// (N2 verified, N3 and N4 estimated, N5 partial, no N1).
/// </summary>
public sealed class DemoService(IRepository repo, IEventBus bus, SeedBundle seed, IngestService ingest, LinkService links, VerifyService verify)
{
    /// <summary>The seed node the demo imports live; it is the one <c>demo-state.json</c> leaves out of the start state.</summary>
    public const string DemoNodeKey = "N1";

    public async Task<Result<IngestResult, IngestError>> IngestDemoAsync(Ctx ctx, CancellationToken ct)
    {
        var spec = seed.DemoState.Nodes.Single(n => n.Key == DemoNodeKey);
        var xml = await File.ReadAllTextAsync(seed.FilePath(spec.InvoiceFile), ct);
        return await ingest.IngestAsync(ctx, xml, new IngestPins(spec.NodeId, spec.InvoiceId, spec.LaneId), ct);
    }

    /// <summary>
    /// Answers for a supplier without a phone: mints a link and submits it through <see cref="VerifyService"/>, so the
    /// audit rows (<c>link.requested</c>, <c>verification.submitted</c>) and events are those of a real answer.
    /// </summary>
    public async Task<Result<VerifyOutcome, VerifyError>> SimulateAnswerAsync(
        Ctx ctx, Guid nodeId, TransportMode? mode, EnergySource? energy, decimal? massT, string? clientAddress, CancellationToken ct)
    {
        var minted = await links.MintAsync(ctx, nodeId, ct);
        if (!minted.IsOk)
        {
            return Result<VerifyOutcome, VerifyError>.Fail(new VerifyError(VerifyErrorCode.NodeNotFound, minted.Error.Message));
        }
        return await verify.SubmitAsync(new VerifyAnswer(minted.Value.Token, mode, energy, massT), clientAddress, ct);
    }

    public async Task ResetAsync(Ctx ctx, CancellationToken ct)
    {
        await repo.SeedAsync(seed, reset: true, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new GraphReset(seq, ts, ctx.WorkspaceId));
    }
}
