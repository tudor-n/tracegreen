using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// The supplier terms at a stable address without a token, so a supplier can read, print and save them before accepting
/// (the printable page <c>/terms/supplier</c>). Public text: no rate limit, nothing personal.
/// </summary>
public static class TermsEndpoints
{
    public static IEndpointRouteBuilder MapTerms(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/terms/supplier", (SeedBundle seed) => TypedResults.Ok(seed.SupplierTerms))
            .WithName("GetSupplierTerms")
            .WithTags("Verify")
            .WithSummary("The current supplier terms in Romanian and English, with their version and the SHA-256 a submit sends back")
            .Produces<SupplierTerms>();
        return app;
    }
}
