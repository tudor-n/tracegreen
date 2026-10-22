using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using TraceGreen.Domain.Model;

namespace TraceGreen.Api.Endpoints;

/// <summary>Multipart form of <c>POST /api/ingest/ubl</c>: one UBL 2.1 XML file in the field <c>file</c>.</summary>
public sealed record IngestUblForm(IFormFile File);

public static class IngestEndpoints
{
    public static IEndpointRouteBuilder MapIngest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ingest/ubl", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("IngestUbl")
            .WithTags("Ingest")
            .WithSummary("Import a UBL 2.1 invoice (multipart field 'file', or the XML as a text/xml body) as an ESTIMATED node")
            .Accepts<IngestUblForm>("multipart/form-data")
            .AddOpenApiOperationTransformer(AddXmlBody)
            .Produces<IngestResult>(StatusCodes.Status201Created)
            .ProducesApiProblem(
                StatusCodes.Status400BadRequest,
                StatusCodes.Status409Conflict,
                StatusCodes.Status413PayloadTooLarge,
                StatusCodes.Status415UnsupportedMediaType,
                StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/ingest/demo", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("IngestDemo")
            .WithTags("Ingest")
            .WithSummary("Import the demo invoice (seed N1) with its fixed node and invoice ids")
            .Produces<IngestResult>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status409Conflict);
        return app;
    }

    /// <summary>A second <c>Accepts</c> would replace the multipart one, so the raw XML body is added to the description here.</summary>
    private static Task AddXmlBody(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        if (operation.RequestBody?.Content is { } content)
        {
            foreach (var mediaType in new[] { "text/xml", "application/xml" })
            {
                content[mediaType] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
            }
        }
        return Task.CompletedTask;
    }
}
