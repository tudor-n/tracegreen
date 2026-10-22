using System.Text.Json.Serialization;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api;

/// <summary>Stable machine-readable error codes. Clients switch on these, never on titles or details.</summary>
public enum ProblemCode
{
    [JsonStringEnumMemberName("TooLarge")] TooLarge,
    [JsonStringEnumMemberName("DoctypeForbidden")] DoctypeForbidden,
    [JsonStringEnumMemberName("NotUblInvoice")] NotUblInvoice,
    [JsonStringEnumMemberName("Schema")] Schema,
    [JsonStringEnumMemberName("Duplicate")] Duplicate,
    [JsonStringEnumMemberName("MissingFile")] MissingFile,
    [JsonStringEnumMemberName("UnsupportedMediaType")] UnsupportedMediaType,
    [JsonStringEnumMemberName("UnsupportedCurrency")] UnsupportedCurrency,
    [JsonStringEnumMemberName("NoLane")] NoLane,
}

/// <summary>RFC 9457 problem details with a typed <see cref="Code"/>; <see cref="NodeId"/> is set on a duplicate import.</summary>
public sealed record ApiProblem(string Type, string Title, int Status, string Detail, ProblemCode Code, Guid? NodeId);

public static class Problems
{
    public const string ContentType = "application/problem+json";

    public static IResult Result(ProblemCode code, string detail, Guid? nodeId = null)
    {
        var (status, title) = code switch
        {
            ProblemCode.TooLarge => (StatusCodes.Status413PayloadTooLarge, "Invoice too large"),
            ProblemCode.DoctypeForbidden => (StatusCodes.Status400BadRequest, "Document type definitions are not accepted"),
            ProblemCode.NotUblInvoice => (StatusCodes.Status400BadRequest, "Not a UBL invoice"),
            ProblemCode.Schema => (StatusCodes.Status422UnprocessableEntity, "Invoice does not match the expected schema"),
            ProblemCode.Duplicate => (StatusCodes.Status409Conflict, "Invoice already imported"),
            ProblemCode.MissingFile => (StatusCodes.Status400BadRequest, "No invoice in the request"),
            ProblemCode.UnsupportedMediaType => (StatusCodes.Status415UnsupportedMediaType, "Send multipart/form-data or text/xml"),
            ProblemCode.UnsupportedCurrency => (StatusCodes.Status422UnprocessableEntity, "Currency not supported"),
            ProblemCode.NoLane => (StatusCodes.Status422UnprocessableEntity, "No freight lane for the supplier country"),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        };
        return Results.Json(new ApiProblem("about:blank", title, status, detail, code, nodeId), ApiJson.Options, ContentType, status);
    }

    public static ProblemCode From(IngestErrorCode code) => code switch
    {
        IngestErrorCode.TooLarge => ProblemCode.TooLarge,
        IngestErrorCode.DoctypeForbidden => ProblemCode.DoctypeForbidden,
        IngestErrorCode.NotUblInvoice => ProblemCode.NotUblInvoice,
        IngestErrorCode.Schema => ProblemCode.Schema,
        IngestErrorCode.Duplicate => ProblemCode.Duplicate,
        IngestErrorCode.UnsupportedCurrency => ProblemCode.UnsupportedCurrency,
        IngestErrorCode.NoLane => ProblemCode.NoLane,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    public static RouteHandlerBuilder ProducesApiProblem(this RouteHandlerBuilder builder, params int[] statusCodes)
    {
        foreach (var status in statusCodes)
        {
            builder.Produces<ApiProblem>(status, ContentType);
        }
        return builder;
    }
}
