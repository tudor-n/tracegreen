using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;

namespace TraceGreen.Domain.Services;

public enum LinkErrorCode { NodeNotFound }

public sealed record LinkError(LinkErrorCode Code, string Message);

/// <summary>Stays inside the API: only <see cref="LinkGrant.Url"/> carries a token to a client.</summary>
public sealed record MintedLink(Guid NodeId, string Token, DateTimeOffset ExpiresAt, string Nonce);

/// <summary>
/// Mints signed single-use verification links (C1–C2): a fresh nonce, the invoice hash and an expiry of
/// <see cref="ServiceOptions.LinkTtl"/>, then a <c>link.requested</c> audit entry that names the node and the expiry but
/// never the token or its nonce. A link a person asks for remembers the keyed hash of their address
/// (<see cref="LinkOriginStore"/>), so an answer from the same network is marked (F7).
/// </summary>
public sealed class LinkService(
    IRepository repo, IEventBus bus, ILinkSigner signer, LinkOriginStore origins, TimeProvider clock, ServiceOptions options)
{
    /// <param name="baseUrl">The browser-facing origin the phone opens, without a trailing slash.</param>
    /// <param name="clientAddress">The address of whoever asked for the link; only its keyed hash is kept, in memory.</param>
    public async Task<Result<LinkGrant, LinkError>> RequestAsync(Ctx ctx, Guid nodeId, string baseUrl, string? clientAddress, CancellationToken ct)
    {
        var minted = await MintAsync(ctx, nodeId, ct);
        if (!minted.IsOk)
        {
            return Result<LinkGrant, LinkError>.Fail(minted.Error);
        }
        if (clientAddress is not null)
        {
            origins.Remember(LinkOriginStore.VerifyKey(minted.Value.Nonce), Hashing.IpHash(clientAddress, options.IpHashKey));
        }
        return Result<LinkGrant, LinkError>.Ok(new LinkGrant(nodeId, $"{baseUrl}/verify/{nodeId:D}?t={Uri.EscapeDataString(minted.Value.Token)}", minted.Value.ExpiresAt));
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
        var nonce = signer.MintNonce();
        var token = signer.Sign(new LinkClaims(node.Id, invoice.InvoiceHash, nonce, expiresAt));

        var payload = new JsonObject
        {
            ["nodeId"] = node.Id.ToString("D"),
            ["expiresAt"] = expiresAt.ToString("O"),
        };
        var entry = await repo.AppendAuditAsync(ctx, prev => AuditChain.Build(prev, ctx.WorkspaceId, now, ctx.Actor, "link.requested", payload), ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, ctx.WorkspaceId, entry));
        return Result<MintedLink, LinkError>.Ok(new MintedLink(node.Id, token, expiresAt, nonce));
    }
}
