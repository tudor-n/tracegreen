using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;
using TraceGreen.Domain.Trust;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// The buyer's own records of a delivery. <see cref="ReceivedOn"/> is the goods-receipt date; for an import from outside
/// the EU, <see cref="CustomsBorderMode"/> and <see cref="CustomsMrn"/> come from the customs declaration (mode of transport
/// at the border, the declaration's MRN). Leave a field out (or null) to clear it.
/// </summary>
public sealed record ReceiptRequest(DateOnly? ReceivedOn = null, TransportMode? CustomsBorderMode = null, string? CustomsMrn = null);

/// <summary>
/// A person's decision on one supplier answer, sent to <c>/api/nodes/{nodeId}/claims/{kind}</c> where the kind is
/// <c>mode</c>, <c>energy</c> or <c>mass</c>. <see cref="ExpectedValue"/> is the claim's <c>value</c> the page showed, so a
/// decision made on a stale page is refused instead of applied to an answer the person never saw.
/// </summary>
public sealed record ClaimReviewRequest(ClaimDecision Decision, string ExpectedValue);

/// <summary>The node's target: a mode, a power source or an alternative supplier. Every field null goes back to the default.</summary>
public sealed record TargetRequest(TransportMode? Mode = null, EnergySource? Energy = null, string? AlternativeId = null);

/// <summary>
/// The buyer's side of the trust model (F7). Each route answers with the node as it now is: its counted figure, trust and
/// target recomputed, <c>node.updated</c> published with fresh KPIs, and an audit entry appended (<c>receipt.recorded</c>,
/// <c>claim.reviewed</c>, <c>target.set</c>). Like the rest of the dashboard the routes have no authentication, so the
/// audit entry names the role ("buyer"). Errors: unknown node → 404 <c>NodeNotFound</c>; a record in the future, more than
/// a year before the invoice, or an MRN that is not 18 characters → 422 <c>InvalidRecord</c>; no such answer on the
/// delivery → 422 <c>NoClaim</c>; the answer changed since the page read it → 409 <c>ClaimChanged</c>; a target mode the
/// lane lacks → 422 <c>ModeNotAvailable</c>, an unknown alternative → 404 <c>AlternativeNotFound</c>, an alternative of
/// another category → 422 <c>InvalidChange</c>, no mass to compute it with → 422 <c>MassUnknown</c>.
/// </summary>
public static class TrustEndpoints
{
    public static IEndpointRouteBuilder MapTrust(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/nodes/{nodeId:guid}/receipt",
                async (Guid nodeId, ReceiptRequest body, TrustService trust, SeedBundle seed, CancellationToken ct) =>
                    (await trust.RecordReceiptAsync(
                        Workspace.Buyer(seed), nodeId, new BuyerRecords(body.ReceivedOn, body.CustomsBorderMode, body.CustomsMrn), ct))
                        .Match(TypedResults.Ok, error => ToProblem(error, nodeId)))
            .WithName("RecordReceipt")
            .WithTags("Graph")
            .WithSummary("Record the buyer's goods-receipt date and customs record for a delivery; recomputes its trust and counted figure")
            .Produces<NodeView>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);

        // The kind is read as text: route values bind enums by their exact .NET name, and the wire spells them in camelCase.
        app.MapPost("/api/nodes/{nodeId:guid}/claims/{kind}",
                async (Guid nodeId, string kind, ClaimReviewRequest body, TrustService trust, SeedBundle seed, CancellationToken ct) =>
                    Enum.TryParse<ClaimKind>(kind, ignoreCase: true, out var claim) && Enum.IsDefined(claim)
                        ? (await trust.ReviewClaimAsync(Workspace.Buyer(seed), nodeId, claim, body.Decision, body.ExpectedValue, ct))
                            .Match(TypedResults.Ok, error => ToProblem(error, nodeId))
                        : Problems.Result(ProblemCode.NoClaim, $"{kind} is not an answer a delivery has; use mode, energy or mass."))
            .WithName("ReviewClaim")
            .WithTags("Graph")
            .WithSummary("Accept or reject one supplier answer (mode, energy or mass) after looking at its evidence; recomputes the counted figure")
            .Produces<NodeView>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        app.MapPut("/api/nodes/{nodeId:guid}/target",
                async (Guid nodeId, TargetRequest body, TrustService trust, SeedBundle seed, CancellationToken ct) =>
                    (await trust.SetTargetAsync(Workspace.Buyer(seed), nodeId, new TargetChoice(body.Mode, body.Energy, body.AlternativeId), ct))
                        .Match(TypedResults.Ok, error => ToProblem(error, nodeId)))
            .WithName("SetTarget")
            .WithTags("Graph")
            .WithSummary("Choose the best state a delivery could reach (mode, power source or alternative supplier); illustrative like every lane figure")
            .Produces<NodeView>()
            .ProducesApiProblem(StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static IResult ToProblem(TrustError error, Guid nodeId) => error.Code switch
    {
        TrustErrorCode.NodeNotFound => Problems.Result(ProblemCode.NodeNotFound, error.Message, nodeId),
        TrustErrorCode.InvalidRecord => Problems.Result(ProblemCode.InvalidRecord, error.Message),
        TrustErrorCode.NoClaim => Problems.Result(ProblemCode.NoClaim, error.Message),
        TrustErrorCode.ClaimChanged => Problems.Result(ProblemCode.ClaimChanged, error.Message, nodeId),
        TrustErrorCode.AlternativeNotFound => Problems.Result(ProblemCode.AlternativeNotFound, error.Message),
        TrustErrorCode.InvalidChange => Problems.Result(ProblemCode.InvalidChange, error.Message),
        TrustErrorCode.ModeNotAvailable => Problems.Result(ProblemCode.ModeNotAvailable, error.Message),
        TrustErrorCode.MassUnknown => Problems.Result(ProblemCode.MassUnknown, error.Message),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, null),
    };
}
