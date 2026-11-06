using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Mapped to HTTP by the portal endpoints: <see cref="InvalidToken"/> 401, <see cref="Expired"/> and
/// <see cref="SupplierGone"/> 410, <see cref="NodeNotFound"/> 404, <see cref="AlreadyConfirmed"/> 409,
/// <see cref="InvalidAnswer"/> 422.
/// </summary>
public enum PortalErrorCode { InvalidToken, Expired, SupplierGone, NodeNotFound, AlreadyConfirmed, InvalidAnswer }

public sealed record PortalError(PortalErrorCode Code, string Message);

/// <summary>
/// The supplier page (F12): one signed link that lists every delivery on the buyer's graph from one supplier and lets the
/// supplier confirm each of them. There is no account: the link is a <c>p1.</c> token, reusable until it expires and
/// anchored on one node, and "the supplier" is everyone with the anchor invoice's seller tax id in the anchor's workspace.
/// An answer goes the way <see cref="DemoService.SimulateAnswerAsync"/> goes: a fresh single-use verification link for
/// that node, submitted through <see cref="VerifyService"/>. So validation, status, plausibility, the verification row,
/// the audit entries and the <c>node.verified</c> event are those of an answer from the phone page.
/// </summary>
public sealed class PortalService(
    IRepository repo,
    IEventBus bus,
    PortalLinkSigner signer,
    LinkService links,
    VerifyService verify,
    SeedBundle seed,
    TimeProvider clock,
    ServiceOptions options)
{
    /// <summary>The actor of the links the page mints for its answers; the answers themselves are the supplier's.</summary>
    public const string Actor = "portal";

    private sealed record Supplier(Ctx Ctx, SupplierNode Anchor, string Key, DateTimeOffset ExpiresAt);

    private sealed record Delivery(SupplierNode Node, Invoice Invoice, Lane Lane);

    /// <param name="baseUrl">The browser-facing origin the phone opens, without a trailing slash.</param>
    public async Task<Result<LinkGrant, PortalError>> RequestAsync(Ctx ctx, Guid nodeId, string baseUrl, CancellationToken ct)
    {
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
        if (node is null || invoice is null)
        {
            return Result<LinkGrant, PortalError>.Fail(new PortalError(PortalErrorCode.NodeNotFound, $"No node {nodeId} on the graph."));
        }

        var now = clock.GetUtcNow();
        // The token carries whole seconds, so the answer states the same instant the token does.
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds((now + options.LinkTtl).ToUnixTimeSeconds());
        var token = signer.Sign(new PortalClaims(node.Id, expiresAt));

        var payload = new JsonObject
        {
            ["nodeId"] = node.Id.ToString("D"),
            ["expiresAt"] = expiresAt.ToString("O"),
        };
        var entry = await repo.AppendAuditAsync(ctx, prev => AuditChain.Build(prev, ctx.WorkspaceId, now, ctx.Actor, "portal.requested", payload), ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, ctx.WorkspaceId, entry));
        return Result<LinkGrant, PortalError>.Ok(new LinkGrant(node.Id, $"{baseUrl}/supplier?t={Uri.EscapeDataString(token)}", expiresAt));
    }

    public async Task<Result<PortalView, PortalError>> ViewAsync(string? token, CancellationToken ct)
    {
        var opened = await OpenAsync(token, ct);
        if (!opened.IsOk)
        {
            return Result<PortalView, PortalError>.Fail(opened.Error);
        }
        var supplier = opened.Value;
        var buyer = await repo.GetBuyerAsync(supplier.Ctx, ct);
        var deliveries = await DeliveriesAsync(supplier, ct);
        var lastEnergy = await LastEnergyAsync(supplier.Ctx, deliveries, ct);

        var items = deliveries
            .Select(d => Item(d, lastEnergy))
            .OrderBy(i => i.Status == NodeStatus.Verified)
            .ThenByDescending(i => i.IssueDate)
            .ThenBy(i => i.InvoiceNumber, StringComparer.Ordinal)
            .ToList();
        return Result<PortalView, PortalError>.Ok(new PortalView(supplier.Anchor.Label, buyer.Name, supplier.ExpiresAt, items));
    }

    /// <param name="clientAddress">Passed on to <see cref="VerifyService.SubmitAsync"/>, which stores only its hash.</param>
    public async Task<Result<VerifyOutcome, PortalError>> AnswerAsync(
        string? token, Guid nodeId, TransportMode? mode, EnergySource? energy, string? clientAddress, CancellationToken ct)
    {
        var opened = await OpenAsync(token, ct);
        if (!opened.IsOk)
        {
            return Fail(opened.Error);
        }
        var supplier = opened.Value;

        var node = await repo.GetNodeAsync(supplier.Ctx, nodeId, ct);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(supplier.Ctx, node.InvoiceId, ct);
        var lane = node is null ? null : seed.Lanes.FirstOrDefault(l => l.Id == node.LaneId);
        // Another supplier's node answers exactly like a missing one, so the link reveals nothing beyond its own list.
        if (node is null || invoice is null || lane is null || SupplierKey(node, invoice) != supplier.Key)
        {
            return Fail(new PortalError(PortalErrorCode.NodeNotFound, "No such delivery on this supplier page."));
        }
        if (node.Status == NodeStatus.Verified)
        {
            return Fail(new PortalError(PortalErrorCode.AlreadyConfirmed, "This delivery is already confirmed."));
        }
        // Checked before a link is minted, so a refused answer leaves no link.requested entry behind.
        if (mode is null && energy is null)
        {
            return Fail(new PortalError(PortalErrorCode.InvalidAnswer, "Answer at least one question."));
        }
        if (mode is { } m && !Simulator.AvailableModes(lane).Contains(m))
        {
            return Fail(new PortalError(PortalErrorCode.InvalidAnswer, $"Lane {lane.Id} has no {m.ToString().ToLowerInvariant()} leg."));
        }

        var minted = await links.MintAsync(supplier.Ctx, nodeId, ct);
        if (!minted.IsOk)
        {
            return Fail(new PortalError(PortalErrorCode.NodeNotFound, minted.Error.Message));
        }
        var submitted = await verify.SubmitAsync(new VerifyAnswer(minted.Value.Token, mode, energy, null), clientAddress, ct);
        return submitted.IsOk ? Result<VerifyOutcome, PortalError>.Ok(submitted.Value) : Fail(From(submitted.Error));
    }

    /// <summary>
    /// The seller's tax id within its country, so "RO99999984" and "99999984" from a Romanian seller are the same
    /// supplier; the supplier's name when an invoice carries no tax id.
    /// </summary>
    public static string SupplierKey(SupplierNode node, Invoice invoice)
    {
        var country = node.Country.Trim().ToUpperInvariant();
        var taxId = string.Concat(invoice.VendorCui.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (taxId.Length == 0)
        {
            return $"name|{node.Label.Trim().ToUpperInvariant()}";
        }
        if (country.Length > 0 && taxId.Length > country.Length && taxId.StartsWith(country, StringComparison.Ordinal))
        {
            taxId = taxId[country.Length..];
        }
        return $"tax|{country}|{taxId}";
    }

    private async Task<Result<Supplier, PortalError>> OpenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
        {
            return Result<Supplier, PortalError>.Fail(Invalid());
        }
        var verified = signer.Verify(token, clock.GetUtcNow());
        if (!verified.IsOk)
        {
            return Result<Supplier, PortalError>.Fail(verified.Error == TokenError.Expired
                ? new PortalError(PortalErrorCode.Expired, "This link has expired.")
                : Invalid());
        }

        var anchor = await repo.FindNodeAsync(verified.Value.AnchorNodeId, ct);
        var ctx = new Ctx(anchor?.WorkspaceId ?? Guid.Empty, Actor);
        var invoice = anchor is null ? null : await repo.GetInvoiceAsync(ctx, anchor.InvoiceId, ct);
        if (anchor is null || invoice is null)
        {
            return Result<Supplier, PortalError>.Fail(
                new PortalError(PortalErrorCode.SupplierGone, "The delivery this link was made for is no longer on the graph."));
        }
        return Result<Supplier, PortalError>.Ok(new Supplier(ctx, anchor, SupplierKey(anchor, invoice), verified.Value.ExpiresAt));
    }

    private async Task<List<Delivery>> DeliveriesAsync(Supplier supplier, CancellationToken ct)
    {
        var deliveries = new List<Delivery>();
        foreach (var node in await repo.ListNodesAsync(supplier.Ctx, ct))
        {
            var invoice = await repo.GetInvoiceAsync(supplier.Ctx, node.InvoiceId, ct);
            var lane = seed.Lanes.FirstOrDefault(l => l.Id == node.LaneId);
            if (invoice is not null && lane is not null && SupplierKey(node, invoice) == supplier.Key)
            {
                deliveries.Add(new Delivery(node, invoice, lane));
            }
        }
        return deliveries;
    }

    /// <summary>The power source in the supplier's most recent answer that gave one: it is the same factory whatever the delivery.</summary>
    private async Task<EnergySource?> LastEnergyAsync(Ctx ctx, IReadOnlyList<Delivery> deliveries, CancellationToken ct)
    {
        Verification? latest = null;
        foreach (var delivery in deliveries)
        {
            foreach (var answer in await repo.ListVerificationsAsync(ctx, delivery.Node.Id, ct))
            {
                if (answer.EnergySource is not null && (latest is null || answer.CreatedAt > latest.CreatedAt))
                {
                    latest = answer;
                }
            }
        }
        return latest?.EnergySource;
    }

    private static PortalItem Item(Delivery delivery, EnergySource? lastEnergy)
    {
        var (node, invoice, lane) = delivery;
        var modes = Simulator.AvailableModes(lane);
        var answered = node.Status != NodeStatus.Estimated;
        TransportMode? answeredMode = answered ? node.TransportMode : null;
        EnergySource? answeredEnergy = answered ? node.EnergySource : null;
        var defaultMode = answeredMode is { } m && modes.Contains(m) ? m : lane.ModeDefault;
        return new PortalItem(
            NodeId: node.Id,
            Status: node.Status,
            InvoiceNumber: invoice.Extracted.Number,
            IssueDate: invoice.IssueDate,
            ItemDescription: InvoiceFacts.DominantLine(invoice.Extracted.Lines).Description,
            Origin: lane.Origin,
            Dest: lane.Dest,
            Modes: modes,
            DefaultMode: defaultMode,
            EnergySources: Enum.GetValues<EnergySource>(),
            DefaultEnergy: answeredEnergy ?? lastEnergy ?? EnergySource.Grid,
            AnsweredMode: answeredMode,
            AnsweredEnergy: answeredEnergy);
    }

    private static PortalError Invalid() => new(PortalErrorCode.InvalidToken, "This link is not valid.");

    private static PortalError From(VerifyError error) => new(
        error.Code switch
        {
            VerifyErrorCode.InvalidAnswer => PortalErrorCode.InvalidAnswer,
            VerifyErrorCode.NodeNotFound => PortalErrorCode.NodeNotFound,
            VerifyErrorCode.Expired => PortalErrorCode.Expired,
            VerifyErrorCode.InvoiceChanged => PortalErrorCode.SupplierGone,
            VerifyErrorCode.AlreadyUsed => PortalErrorCode.AlreadyConfirmed,
            _ => PortalErrorCode.InvalidToken,
        },
        error.Message);

    private static Result<VerifyOutcome, PortalError> Fail(PortalError error) => Result<VerifyOutcome, PortalError>.Fail(error);
}
