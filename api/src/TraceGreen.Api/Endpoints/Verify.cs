using TraceGreen.Domain.Model;

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
        app.MapPost("/api/request-link", (RequestLinkRequest body) => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("RequestLink")
            .WithTags("Verify")
            .WithSummary("Mint a signed single-use verification link for a node and record link.requested in the audit log")
            .Produces<RequestLinkResponse>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/verify/context", (string? t) => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("GetVerifyContext")
            .WithTags("Verify")
            .WithSummary("What the supplier page shows for a link: supplier, item, the lane's modes and the defaults; nothing personal")
            .Produces<VerifyContext>()
            .ProducesApiProblem(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status409Conflict,
                StatusCodes.Status410Gone,
                StatusCodes.Status429TooManyRequests);

        app.MapPost("/api/verify", (VerifyRequest body) => Results.StatusCode(StatusCodes.Status501NotImplemented))
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
}
