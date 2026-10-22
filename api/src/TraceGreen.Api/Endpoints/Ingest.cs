using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using TraceGreen.Domain.Model;

namespace TraceGreen.Api.Endpoints;

/// <summary><c>POST /api/ingest/ubl</c> takes one UBL 2.1 XML file in the multipart field <c>file</c>, or the XML as the body.</summary>
public static class IngestEndpoints
{
    public static IEndpointRouteBuilder MapIngest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ingest/ubl", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("IngestUbl")
            .WithTags("Ingest")
            .WithSummary("Import a UBL 2.1 invoice (multipart field 'file', or the XML as a text/xml body) as an ESTIMATED node")
            .AddOpenApiOperationTransformer(DescribeBody)
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

    /// <summary>
    /// Describes the two accepted bodies. <c>Accepts</c> metadata is not used: the endpoint matcher would then answer other
    /// content types with an empty 415 before the handler could return an <see cref="ApiProblem"/>.
    /// </summary>
    private static Task DescribeBody(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var xml = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
        var form = new OpenApiMediaType
        {
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string> { "file" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                },
            },
        };
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = form,
                ["text/xml"] = xml,
                ["application/xml"] = xml,
            },
        };
        return Task.CompletedTask;
    }
}
