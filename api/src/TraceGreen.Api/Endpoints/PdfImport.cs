using Microsoft.AspNetCore.Mvc;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;

namespace TraceGreen.Api.Endpoints;

/// <summary>The person's checked fields for a draft from <c>POST /api/ingest/pdf/extract</c>.</summary>
public sealed record PdfConfirmRequest(string? DraftId, PdfInvoiceForm? Invoice);

/// <summary>
/// The PDF import (F9) in two steps. <c>extract</c> takes one PDF in the multipart field <c>file</c> (at most 3 MB): the
/// AI service reads its text layer and copies the fields into a draft, labelled "AI-extracted · check fields", with
/// which fields the PDF's text shows; nothing is imported. <c>confirm</c> takes the draft's id and the fields as the
/// person checked them, validates them like any invoice and imports them; the audit entry records which fields the
/// person changed. Errors: over 3 MB → 413 <c>TooLarge</c>; not a PDF → 415 <c>NotPdf</c>; no text layer → 422
/// <c>PdfNoText</c>; unreadable → 422 <c>PdfUnreadable</c>; no model → 503 <c>ExtractionUnavailable</c>; already imported
/// → 409 <c>Duplicate</c>; draft gone → 410 <c>DraftExpired</c>; a field that does not hold → 422 <c>InvalidInvoiceFields</c>.
/// </summary>
public static class PdfImportEndpoints
{
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    public static IEndpointRouteBuilder MapPdfImport(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ingest/pdf/extract", async (HttpRequest request, PdfImportService pdf, SeedBundle seed, CancellationToken ct) =>
            {
                byte[] bytes;
                try
                {
                    if (!request.HasFormContentType)
                    {
                        return Problems.Result(ProblemCode.UnsupportedMediaType, "Send the PDF as multipart/form-data in the field 'file'.");
                    }
                    var form = await request.ReadFormAsync(ct);
                    var file = form.Files.GetFile("file");
                    if (file is null || file.Length == 0)
                    {
                        return Problems.Result(ProblemCode.MissingFile, "Send the PDF in the multipart field 'file'.");
                    }
                    if (file.Length > PdfImportService.MaxPdfBytes)
                    {
                        return Problems.Result(ProblemCode.TooLarge, $"The PDF is over {PdfImportService.MaxPdfBytes / (1024 * 1024)} MB.");
                    }
                    using var buffer = new MemoryStream();
                    await file.CopyToAsync(buffer, ct);
                    bytes = buffer.ToArray();
                }
                catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
                {
                    return Problems.Result(ProblemCode.TooLarge, "The upload is too large.");
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException && !ct.IsCancellationRequested)
                {
                    return Problems.Result(ProblemCode.MissingFile, "The upload was incomplete; send the file again.");
                }
                return (await pdf.ExtractAsync(Workspace.Buyer(seed), bytes, ct)).Match(
                    view => Results.Ok(view),
                    error => Problems.Result(Problems.From(error.Code), error.Message, error.ExistingNodeId));
            })
            .RequireRateLimiting(ChatEndpoints.RateLimitPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .WithName("ExtractPdfInvoice")
            .WithTags("Ingest")
            .WithSummary("Read a PDF invoice's fields into a draft for a person to check; nothing is imported")
            .Produces<PdfDraftView>()
            .ProducesApiProblem(
                StatusCodes.Status400BadRequest,
                StatusCodes.Status409Conflict,
                StatusCodes.Status413PayloadTooLarge,
                StatusCodes.Status415UnsupportedMediaType,
                StatusCodes.Status422UnprocessableEntity,
                StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/ingest/pdf/confirm", async (PdfConfirmRequest body, PdfImportService pdf, SeedBundle seed, CancellationToken ct) =>
                (await pdf.ConfirmAsync(Workspace.Buyer(seed), body.DraftId, body.Invoice, ct)).Match(
                    ok => Results.Created((string?)null, ok),
                    error => Problems.Result(Problems.From(error.Code), error.Message, error.ExistingNodeId)))
            .WithName("ConfirmPdfInvoice")
            .WithTags("Ingest")
            .WithSummary("Import a PDF invoice's fields as a person checked them, as an ESTIMATED node")
            .Produces<IngestResult>(StatusCodes.Status201Created)
            .ProducesApiProblem(StatusCodes.Status409Conflict, StatusCodes.Status410Gone, StatusCodes.Status422UnprocessableEntity);
        return app;
    }
}
