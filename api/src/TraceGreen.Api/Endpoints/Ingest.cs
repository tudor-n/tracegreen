using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary><c>POST /api/ingest/ubl</c> takes one UBL 2.1 XML file in the multipart field <c>file</c>, or the XML as the body.</summary>
public static class IngestEndpoints
{
    /// <summary>Matches nginx's <c>client_max_body_size</c>; the parser's own 2 M-character cap applies after decoding.</summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    public static IEndpointRouteBuilder MapIngest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ingest/ubl", async (HttpRequest request, IngestService ingest, SeedBundle seed, CancellationToken ct) =>
            {
                var body = await ReadInvoiceAsync(request, ct);
                if (!body.IsOk)
                {
                    return Problems.Result(body.Error.Code, body.Error.Detail);
                }
                return ToHttp(await ingest.IngestAsync(Workspace.Buyer(seed), body.Value, pins: null, ct));
            })
            .WithName("IngestUbl")
            .WithTags("Ingest")
            .WithSummary("Import a UBL 2.1 invoice (multipart field 'file', or the XML as a text/xml body) as an ESTIMATED node")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .AddOpenApiOperationTransformer(DescribeBody)
            .Produces<IngestResult>(StatusCodes.Status201Created)
            .ProducesApiProblem(
                StatusCodes.Status400BadRequest,
                StatusCodes.Status409Conflict,
                StatusCodes.Status413PayloadTooLarge,
                StatusCodes.Status415UnsupportedMediaType,
                StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/ingest/demo", async (DemoService demo, SeedBundle seed, CancellationToken ct) =>
                ToHttp(await demo.IngestDemoAsync(Workspace.Buyer(seed), ct)))
            .WithName("IngestDemo")
            .WithTags("Ingest")
            .WithSummary("Import the demo invoice (seed N1) with its fixed node and invoice ids")
            .Produces<IngestResult>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static IResult ToHttp(Result<IngestResult, IngestError> result) => result.Match(
        ok => TypedResults.Created((string?)null, ok),
        error => Problems.Result(Problems.From(error.Code), error.Message, error.ExistingNodeId));

    private sealed record BodyError(ProblemCode Code, string Detail);

    /// <summary>The invoice text from the multipart field <c>file</c> or from an XML body, refusing anything over <see cref="MaxBodyBytes"/>.</summary>
    private static async Task<Result<string, BodyError>> ReadInvoiceAsync(HttpRequest request, CancellationToken ct)
    {
        try
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0)
                {
                    return Fail(ProblemCode.MissingFile, "Send the invoice in the multipart field 'file'.");
                }
                if (file.Length > MaxBodyBytes)
                {
                    return TooLarge();
                }
                await using var stream = file.OpenReadStream();
                return await ReadCappedAsync(stream, ct);
            }

            var mediaType = MediaTypeHeaderValue.TryParse(request.ContentType, out var parsed) ? parsed.MediaType.Value : null;
            if (mediaType is not ("text/xml" or "application/xml"))
            {
                return Fail(ProblemCode.UnsupportedMediaType, $"Content type {request.ContentType ?? "(none)"} is not accepted.");
            }
            return await ReadCappedAsync(request.Body, ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (InvalidDataException)
        {
            return Fail(ProblemCode.MissingFile, "The multipart body could not be read.");
        }
    }

    private static async Task<Result<string, BodyError>> ReadCappedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return TooLarge();
            }
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0)
        {
            return Fail(ProblemCode.MissingFile, "The request body is empty.");
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Result<string, BodyError>.Ok(await reader.ReadToEndAsync(ct));
    }

    private static Result<string, BodyError> TooLarge() =>
        Fail(ProblemCode.TooLarge, $"The invoice exceeds {MaxBodyBytes / (1024 * 1024)} MB.");

    private static Result<string, BodyError> Fail(ProblemCode code, string detail) =>
        Result<string, BodyError>.Fail(new BodyError(code, detail));

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
