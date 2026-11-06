using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Trust;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Maps to HTTP: <see cref="NodeNotFound"/> and <see cref="AlternativeNotFound"/> 404; <see cref="InvalidRecord"/>,
/// <see cref="NoClaim"/>, <see cref="InvalidChange"/>, <see cref="ModeNotAvailable"/> and <see cref="MassUnknown"/> 422;
/// <see cref="ClaimChanged"/> 409.
/// </summary>
public enum TrustErrorCode { NodeNotFound, InvalidRecord, NoClaim, ClaimChanged, AlternativeNotFound, InvalidChange, ModeNotAvailable, MassUnknown }

public sealed record TrustError(TrustErrorCode Code, string Message);

/// <summary>
/// The buyer's side of the trust model (F7): the buyer's own records of a delivery (goods-receipt date; for an import, the
/// customs declaration's mode at the border and MRN), a person's decision on one claim, and the node's target. Each write
/// recomputes the counted figure from the node as the repository holds it under the write's lock, appends its audit entry
/// in the same write, then publishes <c>node.updated</c> with fresh KPIs and <c>audit.appended</c>. The dashboard has no
/// login, so the actor is the role.
/// </summary>
public sealed partial class TrustService(
    IRepository repo, IEventBus bus, SeedBundle seed, NodeViewMapper mapper, GraphService graph, TimeProvider clock)
{
    public const string ReceiptAction = "receipt.recorded";

    public const string ClaimAction = "claim.reviewed";

    public const string TargetAction = "target.set";

    private sealed record Loaded(SupplierNode Node, Invoice Invoice, Lane Lane);

    /// <summary>Replaces the delivery's buyer records; all fields null clears them.</summary>
    public async Task<Result<NodeView, TrustError>> RecordReceiptAsync(Ctx ctx, Guid nodeId, BuyerRecords records, CancellationToken ct)
    {
        if (await LoadAsync(ctx, nodeId, ct) is not { } loaded)
        {
            return NotFound(nodeId);
        }
        var now = AuditChain.Normalize(clock.GetUtcNow());
        var normalized = records with { CustomsMrn = records.CustomsMrn is { } mrn ? string.Concat(mrn.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant() : null };
        if (normalized.CustomsMrn is "")
        {
            normalized = normalized with { CustomsMrn = null };
        }
        if (RecordProblem(normalized, loaded.Invoice, now) is { } problem)
        {
            return Fail(TrustErrorCode.InvalidRecord, problem);
        }

        TrustWrite? Write(SupplierNode current)
        {
            var before = current.Trust ?? TrustInputs.None;
            var records = normalized.IsEmpty ? null : normalized;
            // New records are new evidence about the transport: a decision on the mode or the mass made without them is
            // dropped, so a record that contradicts an accepted answer counts. Unchanged records keep the decisions.
            var changed = before.Records != records;
            var trust = before with
            {
                Records = records,
                ModeReview = changed ? null : before.ModeReview,
                MassReview = changed ? null : before.MassReview,
            };
            var payload = new JsonObject
            {
                ["nodeId"] = current.Id.ToString("D"),
                ["receivedOn"] = normalized.ReceivedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["customsBorderMode"] = normalized.CustomsBorderMode is { } mode ? TrustAssessor.Name(mode) : null,
                ["customsMrn"] = normalized.CustomsMrn,
            };
            return new TrustWrite(trust, Counted(current with { Trust = trust }, loaded), current.Target, Audit(ctx, now, ReceiptAction, payload));
        }
        return await WriteAsync(ctx, nodeId, loaded, Write, refusal: null, ct);
    }

    /// <summary>
    /// A person accepts or rejects one claim. <paramref name="expectedValue"/> is the claim's value the page showed
    /// (<see cref="ClaimAssessment.Value"/>); a claim that changed since is refused rather than decided blind.
    /// </summary>
    public async Task<Result<NodeView, TrustError>> ReviewClaimAsync(
        Ctx ctx, Guid nodeId, ClaimKind kind, ClaimDecision decision, string expectedValue, CancellationToken ct)
    {
        if (await LoadAsync(ctx, nodeId, ct) is not { } loaded)
        {
            return NotFound(nodeId);
        }
        var now = AuditChain.Normalize(clock.GetUtcNow());
        TrustError? refusal = null;

        TrustWrite? Write(SupplierNode current)
        {
            var claim = Assess(current, loaded)?.Assessment.Claims.FirstOrDefault(c => c.Kind == kind);
            if (claim is null)
            {
                refusal = new(TrustErrorCode.NoClaim, $"The supplier has given no {Name(kind)} answer for this delivery.");
                return null;
            }
            if (!string.Equals(claim.Value, expectedValue, StringComparison.Ordinal))
            {
                refusal = new(TrustErrorCode.ClaimChanged, $"The {Name(kind)} answer is now {claim.Value}, not {expectedValue}; reload and decide again.");
                return null;
            }
            var trust = (current.Trust ?? TrustInputs.None).WithReview(kind, decision);
            var payload = new JsonObject
            {
                ["nodeId"] = current.Id.ToString("D"),
                ["claim"] = Name(kind),
                ["value"] = claim.Value,
                ["decision"] = decision == ClaimDecision.Accepted ? "accepted" : "rejected",
                ["fromLevel"] = Camel(claim.Level.ToString()),
            };
            return new TrustWrite(trust, Counted(current with { Trust = trust }, loaded), current.Target, Audit(ctx, now, ClaimAction, payload));
        }
        return await WriteAsync(ctx, nodeId, loaded, Write, () => refusal, ct);
    }

    /// <summary>Sets the node's target; a choice with every field null goes back to the default.</summary>
    public async Task<Result<NodeView, TrustError>> SetTargetAsync(Ctx ctx, Guid nodeId, TargetChoice choice, CancellationToken ct)
    {
        if (await LoadAsync(ctx, nodeId, ct) is not { } loaded)
        {
            return NotFound(nodeId);
        }
        if (TargetFigure.Problem(loaded.Node, loaded.Lane, choice, seed) is { } problem)
        {
            return Fail(problem.Problem switch
            {
                TargetProblem.AlternativeNotFound => TrustErrorCode.AlternativeNotFound,
                TargetProblem.ModeNotAvailable => TrustErrorCode.ModeNotAvailable,
                TargetProblem.MassUnknown => TrustErrorCode.MassUnknown,
                _ => TrustErrorCode.InvalidChange,
            }, problem.Message);
        }
        var now = AuditChain.Normalize(clock.GetUtcNow());

        TrustWrite Write(SupplierNode current)
        {
            var payload = new JsonObject
            {
                ["nodeId"] = current.Id.ToString("D"),
                ["mode"] = choice.Mode is { } mode ? TrustAssessor.Name(mode) : null,
                ["energy"] = choice.Energy is { } energy ? TrustAssessor.Name(energy) : null,
                ["alternativeId"] = choice.AlternativeId,
            };
            return new TrustWrite(current.Trust, current.CountedKg, choice.IsEmpty ? null : choice, Audit(ctx, now, TargetAction, payload));
        }
        return await WriteAsync(ctx, nodeId, loaded, Write, refusal: null, ct);
    }

    private async Task<Loaded?> LoadAsync(Ctx ctx, Guid nodeId, CancellationToken ct)
    {
        var node = await repo.GetNodeAsync(ctx, nodeId, ct);
        var invoice = node is null ? null : await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
        var lane = node is null ? null : seed.Lanes.FirstOrDefault(l => l.Id == node.LaneId);
        return node is null || invoice is null || lane is null ? null : new Loaded(node, invoice, lane);
    }

    private async Task<Result<NodeView, TrustError>> WriteAsync(
        Ctx ctx, Guid nodeId, Loaded loaded, TrustUpdater update, Func<TrustError?>? refusal, CancellationToken ct)
    {
        var written = await repo.UpdateTrustAsync(ctx, nodeId, update, ct);
        switch (written.Status)
        {
            case UpdateTrustStatus.NodeNotFound:
                return NotFound(nodeId);
            case UpdateTrustStatus.Refused:
                return Result<NodeView, TrustError>.Fail(refusal?.Invoke() ?? new TrustError(TrustErrorCode.InvalidChange, "The change was refused."));
        }
        if (written is not { Node: { } node, Entry: { } entry })
        {
            throw new InvalidOperationException("The repository reported a trust write without the node or its audit entry.");
        }
        var view = mapper.Map(node, loaded.Invoice);
        var kpis = await graph.KpisAsync(ctx, ct);
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new NodeUpdated(seq, ts, ctx.WorkspaceId, view, kpis));
        bus.Publish(ctx.WorkspaceId, (seq, ts) => new AuditAppended(seq, ts, ctx.WorkspaceId, entry));
        return Result<NodeView, TrustError>.Ok(view);
    }

    private TrustResult? Assess(SupplierNode node, Loaded loaded) =>
        TrustAssessor.Assess(node, loaded.Invoice, loaded.Lane, seed.CategoryOf(node.CategoryId), mapper.Factors);

    private decimal? Counted(SupplierNode node, Loaded loaded) => Assess(node, loaded)?.CountedKg;

    private static AuditBuilder Audit(Ctx ctx, DateTimeOffset now, string action, JsonObject payload) =>
        prev => AuditChain.Build(prev, ctx.WorkspaceId, now, ctx.Actor, action, payload);

    /// <summary>A receipt neither in the future nor more than a year before the invoice; an MRN shaped as one (18 characters).</summary>
    private static string? RecordProblem(BuyerRecords records, Invoice invoice, DateTimeOffset now)
    {
        if (records.ReceivedOn is { } received)
        {
            if (received > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1))
            {
                return "The receipt date is in the future.";
            }
            if (received < invoice.IssueDate.AddDays(-366))
            {
                return "The receipt date is more than a year before the invoice.";
            }
        }
        if (records.CustomsMrn is { } mrn && !MrnShape().IsMatch(mrn))
        {
            return "An MRN is 18 characters: 2 digits for the year, 2 letters for the country, then 14 letters and digits.";
        }
        return null;
    }

    private static string Name(ClaimKind kind) => kind.ToString().ToLowerInvariant();

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static Result<NodeView, TrustError> NotFound(Guid nodeId) => Fail(TrustErrorCode.NodeNotFound, $"No node {nodeId} on the graph.");

    private static Result<NodeView, TrustError> Fail(TrustErrorCode code, string message) =>
        Result<NodeView, TrustError>.Fail(new TrustError(code, message));

    [GeneratedRegex("^[0-9]{2}[A-Z]{2}[A-Z0-9]{14}$")]
    private static partial Regex MrnShape();
}
