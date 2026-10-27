using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;

namespace TraceGreen.Domain.Services;

public enum LinkErrorCode { NodeNotFound }

public sealed record LinkError(LinkErrorCode Code, string Message);

/// <summary>Stays inside the API: only <see cref="LinkGrant.Url"/> carries a token to a client.</summary>
public sealed record MintedLink(Guid NodeId, string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Mints signed single-use verification links (C1–C2): a fresh nonce, the invoice hash and an expiry of
/// <see cref="ServiceOptions.LinkTtl"/>, then a <c>link.requested</c> audit entry that names the node and the expiry but
/// never the token or its nonce.
/// </summary>
public sealed class LinkService(IRepository repo, IEventBus bus, ILinkSigner signer, TimeProvider clock, ServiceOptions options)
{
    /// <param name="baseUrl">The browser-facing origin the phone opens, without a trailing slash.</param>
    public async Task<Result<LinkGrant, LinkError>> RequestAsync(Ctx ctx, Guid nodeId, string baseUrl, CancellationToken ct)
    {
        var minted = await MintAsync(ctx, nodeId, ct);
        return minted.IsOk
            ? Result<LinkGrant, LinkError>.Ok(new LinkGrant(nodeId, $"{baseUrl}/verify/{nodeId:D}?t={Uri.EscapeDataString(minted.Value.Token)}", minted.Value.ExpiresAt))
            : Result<LinkGrant, LinkError>.Fail(minted.Error);
    }

    /// <summary>The token alone, with the same audit entry; for the demo path that answers without a phone.</summary>
    public async Task<Result<MintedLink, LinkError>> MintAsync(Ctx ctx, Guid nodeId, CancellationToken ct)
    {
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
        if (node is null || invoice is null)
        {
            return Result<MintedLink, LinkError>.Fail(new LinkError(LinkErrorCode.NodeNotFound, $"No node {nodeId} on the graph."));
        }

        var now = clock.GetUtcNow();
        // The token carries whole seconds, so the answer states the same instant the token does.
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds((now + options.LinkTtl).ToUnixTimeSeconds());
        var token = signer.Sign(new LinkClaims(node.Id, invoice.InvoiceHash, signer.MintNonce(), expiresAt));

        var payload = new JsonObject
        {
            ["nodeId"] = node.Id.ToString("D"),
            ["expiresAt"] = expiresAt.ToString("O"),
        };
        var entry = await repo.AppendAuditAsync(ctx, prev => AuditChain.Build(prev, ctx.WorkspaceId, now, ctx.Actor, "link.requested", payload), ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, ctx.WorkspaceId, entry));
        return Result<MintedLink, LinkError>.Ok(new MintedLink(node.Id, token, expiresAt));
    }
}
