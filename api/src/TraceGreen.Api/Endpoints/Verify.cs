using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

public sealed record RequestLinkRequest(Guid NodeId);

/// <summary><see cref="Loopback"/> is true when a phone on the hotspot could not open <see cref="Url"/> (the QR card warns).</summary>
public sealed record RequestLinkResponse(Guid NodeId, string Url, DateTimeOffset ExpiresAt, bool Loopback);

/// <summary><see cref="T"/> is the token from the link's <c>?t=</c>. Leave a question's field out (or null) to skip it.</summary>
public sealed record VerifyRequest(string T, TransportMode? TransportMode = null, EnergySource? EnergySource = null, decimal? MassT = null);

/// <summary>
/// The supplier verification loop (F3). The token is the only credential on <c>/api/verify*</c>; it never appears in a
/// response body, an audit payload or a log line. Status mapping (ruling 5): malformed or bad signature → 401
/// <c>InvalidToken</c>; expired → 410 <c>LinkExpired</c>; invoice changed or gone → 410 <c>InvoiceChanged</c>;
/// replay → 409 <c>AlreadyUsed</c>; over <c>RATE_LIMIT_PER_MIN</c> per client address → 429 <c>RateLimited</c>.
/// </summary>
public static class VerifyEndpoints
{
    public const string RateLimitPolicy = "verify";

    public static IEndpointRouteBuilder MapVerify(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/request-link", async (RequestLinkRequest body, HttpRequest request, AppConfig config, LinkService links, SeedBundle seed, CancellationToken ct) =>
            {
                var baseUrl = BaseUrlResolver.Resolve(request, config.PublicBaseUrl);
                var result = await links.RequestAsync(Workspace.Buyer(seed), body.NodeId, baseUrl.Url, ct);
                return result.Match(
                    grant => TypedResults.Created((string?)null, new RequestLinkResponse(grant.NodeId, grant.Url, grant.ExpiresAt, baseUrl.Loopback)),
                    error => Problems.Result(ProblemCode.NodeNotFound, error.Message, body.NodeId));
            })
            .WithName("RequestLink")
            .WithTags("Verify")
            .WithSummary("Mint a signed single-use verification link for a node and record link.requested in the audit log")
            .Produces<RequestLinkResponse>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/verify/context", async (string? t, VerifyService verify, CancellationToken ct) =>
                (await verify.ContextAsync(t, ct)).Match(TypedResults.Ok, ToProblem))
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("GetVerifyContext")
            .WithTags("Verify")
            .WithSummary("What the supplier page shows for a link: supplier, item, the lane's modes and the defaults; nothing personal")
            .Produces<VerifyContext>()
            .ProducesApiProblem(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status409Conflict,
                StatusCodes.Status410Gone,
                StatusCodes.Status429TooManyRequests);

        app.MapPost("/api/verify", async (VerifyRequest body, HttpContext http, VerifyService verify, CancellationToken ct) =>
                (await verify.SubmitAsync(new VerifyAnswer(body.T, body.TransportMode, body.EnergySource, body.MassT), ClientAddress(http), ct))
                    .Match(TypedResults.Ok, ToProblem))
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("SubmitVerification")
            .WithTags("Verify")
            .WithSummary("Submit the supplier's answers: both answered → VERIFIED, one skipped → PARTIAL; warnings never block")
            .Produces<VerifyOutcome>()
            .ProducesApiProblem(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status409Conflict,
                StatusCodes.Status410Gone,
                StatusCodes.Status422UnprocessableEntity,
                StatusCodes.Status429TooManyRequests);
        return app;
    }

    /// <summary>After <c>UseForwardedHeaders</c>: the phone's address when a trusted proxy forwarded it, else the peer's.</summary>
    public static string? ClientAddress(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    public static IResult ToProblem(VerifyError error) => Problems.Result(
        error.Code switch
        {
            VerifyErrorCode.InvalidToken => ProblemCode.InvalidToken,
            VerifyErrorCode.Expired => ProblemCode.LinkExpired,
            VerifyErrorCode.InvoiceChanged => ProblemCode.InvoiceChanged,
            VerifyErrorCode.AlreadyUsed => ProblemCode.AlreadyUsed,
            VerifyErrorCode.InvalidAnswer => ProblemCode.InvalidAnswer,
            VerifyErrorCode.NodeNotFound => ProblemCode.NodeNotFound,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, null),
        },
        error.Message);
}
