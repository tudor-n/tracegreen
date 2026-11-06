using System.Globalization;
using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Plausibility;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Ruling 5 maps these to HTTP: <see cref="InvalidToken"/> 401, <see cref="Expired"/> and <see cref="InvoiceChanged"/>
/// 410, <see cref="AlreadyUsed"/> 409, <see cref="InvalidAnswer"/> 422, <see cref="NodeNotFound"/> 404 (dev path only).
/// </summary>
public enum VerifyErrorCode { InvalidToken, Expired, InvoiceChanged, AlreadyUsed, InvalidAnswer, NodeNotFound }

public sealed record VerifyError(VerifyErrorCode Code, string Message);

/// <summary>
/// The anonymous supplier side of the loop (F3). A token is checked in the signer's order (format, signature, expiry,
/// invoice hash recomputed from the stored extract), then for single use. A submit rebuilds the node from the answers
/// through <see cref="NodeFactory"/>, records the verification and its audit entry atomically (the repository's unique
/// nonce turns a replay into <see cref="VerifyErrorCode.AlreadyUsed"/>), and publishes <c>node.verified</c> with fresh
/// KPIs, then <c>audit.appended</c>. Neither the token, its nonce nor the client address reaches a payload or an event.
/// </summary>
public sealed class VerifyService(
    IRepository repo,
    IEventBus bus,
    ILinkSigner signer,
    SeedBundle seed,
    NodeViewMapper mapper,
    GraphService graph,
    TimeProvider clock,
    ServiceOptions options)
{
    /// <summary>Real tokens are about 190 characters; anything far longer is refused before it is parsed.</summary>
    public const int MaxTokenLength = 512;

    /// <summary>Keeps a confirmed mass inside the bounds ingest applies to quantities, so later sums cannot overflow.</summary>
    public const decimal MaxMassT = 1_000_000m;

    private const string Actor = "supplier";

    private sealed record Link(Ctx Ctx, SupplierNode Node, Invoice Invoice, Lane Lane, string Nonce, DateTimeOffset ExpiresAt);

    public async Task<Result<VerifyContext, VerifyError>> ContextAsync(string? token, CancellationToken ct)
    {
        var opened = await OpenAsync(token, ct);
        if (!opened.IsOk)
        {
            return Result<VerifyContext, VerifyError>.Fail(opened.Error);
        }
        var link = opened.Value;
        if (await IsUsedAsync(link, ct))
        {
            return Result<VerifyContext, VerifyError>.Fail(Used());
        }

        var buyer = await repo.GetBuyerAsync(link.Ctx, ct);
        var line = InvoiceFacts.DominantLine(link.Invoice.Extracted.Lines);
        return Result<VerifyContext, VerifyError>.Ok(new VerifyContext(
            NodeId: link.Node.Id,
            SupplierLabel: link.Node.Label,
            BuyerName: buyer.Name,
            InvoiceNumber: link.Invoice.Extracted.Number,
            IssueDate: link.Invoice.IssueDate,
            ItemDescription: line.Description,
            Origin: link.Lane.Origin,
            Dest: link.Lane.Dest,
            Modes: Simulator.AvailableModes(link.Lane),
            DefaultMode: link.Lane.ModeDefault,
            EnergySources: Enum.GetValues<EnergySource>(),
            DefaultEnergy: EnergySource.Grid,
            MassT: link.Node.MassT,
            ExpiresAt: link.ExpiresAt));
    }

    /// <param name="clientAddress">Hashed with <see cref="ServiceOptions.IpHashKey"/> before it is stored; never stored as is.</param>
    public async Task<Result<VerifyOutcome, VerifyError>> SubmitAsync(VerifyAnswer answer, string? clientAddress, CancellationToken ct)
    {
        var opened = await OpenAsync(answer.Token, ct);
        if (!opened.IsOk)
        {
            return Fail(opened.Error);
        }
        var link = opened.Value;
        // Checked before the answers, so scanning a used link again says "already used" whatever is sent.
        if (await IsUsedAsync(link, ct))
        {
            return Fail(Used());
        }
        if (Invalid(answer, link.Lane) is { } problem)
        {
            return Fail(new VerifyError(VerifyErrorCode.InvalidAnswer, problem));
        }

        var node = link.Node;
        var status = Estimate.DeriveStatus(answer.TransportMode, answer.EnergySource);
        var rebuilt = NodeFactory.NewNode(
            node.Id, link.Invoice, new NodeClassification(node.CategoryId, node.AiConfidence, node.ClassificationSource), link.Lane,
            status, answer.TransportMode, answer.EnergySource, seed, mapper.Factors, node.CreatedAt, answer.MassT ?? node.MassT);
        var patch = new NodePatch(status, rebuilt.ActivityKg, answer.TransportMode, answer.EnergySource, rebuilt.MassT, rebuilt.CostEur, rebuilt.LeadDays);
        var warnings = PlausibilityRules.Check(answer.TransportMode, rebuilt.MassT);

        var now = clock.GetUtcNow();
        var verification = new Verification(
            Ids.NewId(), node.WorkspaceId, node.Id, link.Nonce, answer.TransportMode, answer.EnergySource, answer.MassT, now,
            Hashing.IpHash(clientAddress ?? "unknown", options.IpHashKey), now);
        // Strings only (see IngestService): jsonb may re-render decimals and break the chain hash. No nonce, no address.
        var payload = new JsonObject
        {
            ["nodeId"] = node.Id.ToString("D"),
            ["verificationId"] = verification.Id.ToString("D"),
            ["status"] = status.ToString().ToUpperInvariant(),
            ["transportMode"] = answer.TransportMode?.ToString().ToLowerInvariant(),
            ["energySource"] = answer.EnergySource?.ToString().ToLowerInvariant(),
            ["massTConfirmed"] = answer.MassT?.ToString(CultureInfo.InvariantCulture),
            ["warnings"] = new JsonArray(warnings.Select(w => (JsonNode)JsonValue.Create(Name(w.Code))).ToArray()),
        };
        var recorded = await repo.RecordVerificationAsync(
            link.Ctx, verification, patch, prev => AuditChain.Build(prev, link.Ctx.WorkspaceId, now, Actor, "verification.submitted", payload), ct);
        if (recorded.Status == RecordVerificationStatus.AlreadyUsed || recorded is not { Node: { } patched, Entry: { } entry })
        {
            return Fail(Used());
        }

        var view = mapper.Map(patched, link.Invoice);
        var kpis = await graph.KpisAsync(link.Ctx, ct);
        var verificationView = new VerificationView(verification.Id, verification.TransportMode, verification.EnergySource, verification.MassTConfirmed, verification.ConsumedAt);
        bus.Publish(link.Ctx.WorkspaceId, (seq, ts) => new NodeVerified(seq, ts, link.Ctx.WorkspaceId, view, verificationView, kpis));
        bus.Publish(link.Ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, link.Ctx.WorkspaceId, entry));
        return Result<VerifyOutcome, VerifyError>.Ok(new VerifyOutcome(node.Id, status, warnings));
    }

    /// <summary>
    /// Finds the node the token names, then lets the signer decide. A node that no longer exists (a demo reset removed it)
    /// is checked against a hash no token carries, so a forged token still fails on its signature (401) and only a genuine
    /// one reaches <see cref="VerifyErrorCode.InvoiceChanged"/> (410).
    /// </summary>
    private async Task<Result<Link, VerifyError>> OpenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength || !signer.TryReadNodeId(token, out var nodeId))
        {
            return Result<Link, VerifyError>.Fail(Invalid());
        }

        var node = await repo.FindNodeAsync(nodeId, ct);
        var ctx = new Ctx(node?.WorkspaceId ?? Guid.Empty, Actor);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
        // Recomputed from the stored extract rather than read from the stored hash, so a changed invoice row is caught (C2).
        var expectedHash = invoice is null ? string.Empty : Hashing.InvoiceHash(invoice.Extracted);
        var verified = signer.Verify(token, expectedHash, clock.GetUtcNow());
        if (!verified.IsOk)
        {
            return Result<Link, VerifyError>.Fail(verified.Error switch
            {
                TokenError.Expired => new VerifyError(VerifyErrorCode.Expired, "This link has expired."),
                TokenError.HashMismatch => new VerifyError(VerifyErrorCode.InvoiceChanged, "The invoice changed after this link was made."),
                _ => Invalid(),
            });
        }

        var lane = seed.Lanes.FirstOrDefault(l => l.Id == node!.LaneId);
        if (node is null || invoice is null || lane is null)
        {
            return Result<Link, VerifyError>.Fail(new VerifyError(VerifyErrorCode.InvoiceChanged, "The item this link was made for is no longer on the graph."));
        }
        return Result<Link, VerifyError>.Ok(new Link(ctx, node, invoice, lane, verified.Value.Nonce, verified.Value.ExpiresAt));
    }

    private async Task<bool> IsUsedAsync(Link link, CancellationToken ct) =>
        (await repo.ListVerificationsAsync(link.Ctx, link.Node.Id, ct)).Any(v => string.Equals(v.TokenNonce, link.Nonce, StringComparison.Ordinal));

    private static string? Invalid(VerifyAnswer answer, Lane lane)
    {
        if (answer.TransportMode is null && answer.EnergySource is null)
        {
            return "Answer at least one question.";
        }
        if (answer.TransportMode is { } mode && !Simulator.AvailableModes(lane).Contains(mode))
        {
            return $"Lane {lane.Id} has no {Name(mode)} leg.";
        }
        if (answer.MassT is { } massT && (massT <= 0m || massT > MaxMassT))
        {
            return $"The mass must be above 0 and at most {MaxMassT:0} t.";
        }
        return null;
    }

    private static string Name(TransportMode mode) => mode.ToString().ToLowerInvariant();

    private static string Name(PlausibilityCode code) => code switch
    {
        PlausibilityCode.AirHeavy => "AIR_HEAVY",
        PlausibilityCode.SeaTooFast => "SEA_TOO_FAST",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    private static VerifyError Invalid() => new(VerifyErrorCode.InvalidToken, "This link is not valid.");

    private static VerifyError Used() => new(VerifyErrorCode.AlreadyUsed, "This link has already been used.");

    private static Result<VerifyOutcome, VerifyError> Fail(VerifyError error) => Result<VerifyOutcome, VerifyError>.Fail(error);
}
