using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;
using TraceGreen.Domain.Trust;

namespace TraceGreen.Api.Endpoints;

/// <summary><see cref="T"/> is the token from the supplier page's <c>?t=</c>.</summary>
public sealed record PortalViewRequest(string? T);

/// <summary>
/// Leave a question's field out (or null) to skip it; skipping one makes the delivery PARTIAL. <see cref="AcceptedTerms"/>
/// is <c>terms.sha256</c> from the view, sent once the supplier ticked the terms; without it, or with another value, the
/// answer is refused with 422 <c>TermsNotAccepted</c> and nothing is recorded. <see cref="Evidence"/> is optional and
/// checked as on the phone page (F7).
/// </summary>
public sealed record PortalAnswerRequest(
    string? T, Guid NodeId, TransportMode? TransportMode = null, EnergySource? EnergySource = null, string? AcceptedTerms = null,
    AnswerEvidence? Evidence = null);

/// <summary>
/// The supplier page (F12): one signed link per supplier, reusable until it expires, listing that supplier's deliveries
/// on the buyer's graph. The token travels in request bodies only, so no API URL carries it; like the verify routes it never
/// appears in a response body, an audit payload or a log line. Statuses: malformed or bad signature → 401
/// <c>InvalidToken</c>; expired → 410 <c>LinkExpired</c>; the link's delivery gone → 410 <c>InvoiceChanged</c>; a delivery
/// that is not this supplier's → 404 <c>NodeNotFound</c>; already confirmed → 409 <c>AlreadyConfirmed</c>; answered by
/// someone else between the page's read and this write (two answers at once) → 409 <c>DeliveryChanged</c>; terms not
/// accepted → 422 <c>TermsNotAccepted</c>; over
/// <see cref="RateLimitFactor"/> × <c>RATE_LIMIT_PER_MIN</c> per client address → 429 <c>RateLimited</c>, counted apart
/// from the verify routes.
/// </summary>
public static class PortalEndpoints
{
    public const string RateLimitPolicy = "portal";

    /// <summary>
    /// The page holds a whole list: one call to open it and one per confirmed delivery, where the phone page needs two
    /// calls for its one link. A supplier going through a dozen deliveries in a minute stays inside the allowance.
    /// </summary>
    public const int RateLimitFactor = 3;

    public static IEndpointRouteBuilder MapPortal(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/portal-link", async (RequestLinkRequest body, HttpRequest request, AppConfig config, PortalService portal, SeedBundle seed, CancellationToken ct) =>
            {
                var baseUrl = BaseUrlResolver.Resolve(request, config.PublicBaseUrl);
                var result = await portal.RequestAsync(Workspace.Buyer(seed), body.NodeId, baseUrl.Url, VerifyEndpoints.ClientAddress(request.HttpContext), ct);
                return result.Match(
                    grant => TypedResults.Created((string?)null, new RequestLinkResponse(grant.NodeId, grant.Url, grant.ExpiresAt, baseUrl.Loopback)),
                    error => Problems.Result(ProblemCode.NodeNotFound, error.Message, body.NodeId));
            })
            .WithName("RequestPortalLink")
            .WithTags("Portal")
            .WithSummary("Mint the supplier page link for the supplier of a node and record portal.requested in the audit log")
            .Produces<RequestLinkResponse>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/portal/view", async (PortalViewRequest body, PortalService portal, CancellationToken ct) =>
                (await portal.ViewAsync(body.T, ct)).Match(TypedResults.Ok, ToProblem))
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("GetPortalView")
            .WithTags("Portal")
            .WithSummary("What the supplier page shows: the supplier's deliveries to the buyer, open ones first, with the answers to pre-select and the supplier terms")
            .Produces<PortalView>()
            .ProducesApiProblem(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status410Gone,
                StatusCodes.Status429TooManyRequests);

        app.MapPost("/api/portal/answer", async (PortalAnswerRequest body, HttpContext http, PortalService portal, CancellationToken ct) =>
                (await portal.AnswerAsync(
                    body.T, body.NodeId, body.TransportMode, body.EnergySource, body.AcceptedTerms, VerifyEndpoints.ClientAddress(http),
                    body.Evidence, ct))
                    .Match(TypedResults.Ok, ToProblem))
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("SubmitPortalAnswer")
            .WithTags("Portal")
            .WithSummary("Confirm one delivery from the supplier page with the accepted terms' SHA-256: both answered → VERIFIED, one skipped → PARTIAL; warnings never block. Returns the list as it now is")
            .Produces<PortalAnswerResult>()
            .ProducesApiProblem(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status410Gone,
                StatusCodes.Status422UnprocessableEntity,
                StatusCodes.Status429TooManyRequests);
        return app;
    }

    public static IResult ToProblem(PortalError error) => Problems.Result(
        error.Code switch
        {
            PortalErrorCode.InvalidToken => ProblemCode.InvalidToken,
            PortalErrorCode.Expired => ProblemCode.LinkExpired,
            PortalErrorCode.SupplierGone => ProblemCode.InvoiceChanged,
            PortalErrorCode.NodeNotFound => ProblemCode.NodeNotFound,
            PortalErrorCode.AlreadyConfirmed => ProblemCode.AlreadyConfirmed,
            PortalErrorCode.DeliveryChanged => ProblemCode.DeliveryChanged,
            PortalErrorCode.InvalidAnswer => ProblemCode.InvalidAnswer,
            PortalErrorCode.TermsNotAccepted => ProblemCode.TermsNotAccepted,
            PortalErrorCode.InvalidEvidence => ProblemCode.InvalidEvidence,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, null),
        },
        error.Message);
}
