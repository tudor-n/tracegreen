using System.Globalization;
using System.Security.Cryptography;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Maps to HTTP: <see cref="TooLarge"/> 413, <see cref="NotPdf"/> 415, <see cref="NoText"/> and <see cref="Unreadable"/>
/// and <see cref="InvalidFields"/> 422, <see cref="Unavailable"/> 503, <see cref="DraftExpired"/> 410, <see cref="Duplicate"/>
/// 409. A confirmed draft goes through the UBL import's own checks, which answer with their own codes.
/// </summary>
public enum PdfImportErrorCode { TooLarge, NotPdf, NoText, Unreadable, Unavailable, DraftExpired, InvalidFields, Duplicate }

public sealed record PdfImportError(PdfImportErrorCode Code, string Message, Guid? ExistingNodeId = null);

/// <summary>What the review form shows: the draft's id, the copied fields, which of them the PDF's text shows, and until when.</summary>
public sealed record PdfDraftView(string DraftId, DraftInvoice Invoice, IReadOnlyDictionary<string, bool> Found, string Provider, int Pages, DateTimeOffset ExpiresAt);

/// <summary>The fields as the person confirms them (the same shape as the draft, edited or not).</summary>
public sealed record PdfInvoiceForm(
    string? SupplierName,
    string? SupplierTaxId,
    string? SupplierCountry,
    string? InvoiceNumber,
    string? IssueDate,
    string? Currency,
    string? TotalNet,
    IReadOnlyList<DraftLine>? Lines);

/// <summary>
/// The PDF import (F9) in two steps. Extract: the AI service reads the PDF's text layer and copies its fields; nothing is
/// stored but the draft, held in memory for the person to check. Confirm: the person's fields are validated like any
/// invoice's and imported through <see cref="IngestService.IngestExtractedAsync"/>, with the PDF's SHA-256 as its raw
/// hash, so the same PDF is refused twice; the audit entry records the document kind, the PDF's hash, who extracted it
/// and which fields the person changed. The AI copies figures from the invoice; it never computes one, and nothing it
/// copied is imported until a person confirms it.
/// </summary>
public sealed class PdfImportService(IRepository repo, IAiAssist ai, IngestService ingest, PdfDraftStore drafts, ServiceOptions options)
{
    public const int MaxPdfBytes = 3 * 1024 * 1024;
    public const int MaxLines = 50;

    /// <summary>UN/ECE Recommendation 20 unit codes the form accepts; KGM, TNE and GRM give the activity figure a mass.</summary>
    public static readonly IReadOnlySet<string> UnitCodes = new HashSet<string>(["KGM", "TNE", "GRM", "H87", "C62", "MTR", "LTR", "SET"], StringComparer.Ordinal);

    public async Task<Result<PdfDraftView, PdfImportError>> ExtractAsync(Ctx ctx, byte[] pdf, CancellationToken ct)
    {
        if (pdf.Length > MaxPdfBytes)
        {
            return Fail(PdfImportErrorCode.TooLarge, $"The PDF is over {MaxPdfBytes / (1024 * 1024)} MB.");
        }
        if (pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            return Fail(PdfImportErrorCode.NotPdf, "The file is not a PDF.");
        }
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(pdf));
        if (await DuplicateOf(ctx, sha256, ct) is { } existing)
        {
            return Fail(PdfImportErrorCode.Duplicate, "This PDF is already imported.", existing);
        }

        ExtractOutcome outcome;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(options.AiExtractTimeout);
            outcome = await ai.ExtractPdfAsync(pdf, cts.Token).WaitAsync(options.AiExtractTimeout, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return Fail(PdfImportErrorCode.Unavailable, "No model could read the invoice; import the e-Factura XML instead.");
        }
        switch (outcome.Failure)
        {
            case ExtractFailure.NoText:
                return Fail(PdfImportErrorCode.NoText, "The PDF has no text layer (a scan?); import the e-Factura XML instead.");
            case ExtractFailure.Unreadable:
                return Fail(PdfImportErrorCode.Unreadable, "The PDF could not be read.");
            case ExtractFailure.TooLarge:
                return Fail(PdfImportErrorCode.TooLarge, $"The PDF is over {MaxPdfBytes / (1024 * 1024)} MB.");
            case ExtractFailure.Unavailable:
            case null when outcome.Invoice is null:
                return Fail(PdfImportErrorCode.Unavailable, "No model could read the invoice; import the e-Factura XML instead.");
        }

        var draft = drafts.Add(ctx.WorkspaceId, sha256, outcome.Invoice!, outcome.Found, outcome.Provider, outcome.Pages);
        return Result<PdfDraftView, PdfImportError>.Ok(new PdfDraftView(draft.Id, draft.Invoice, draft.Found, draft.Provider, draft.Pages, draft.ExpiresAt));
    }

    public async Task<Result<IngestResult, PdfImportError>> ConfirmAsync(Ctx ctx, string? draftId, PdfInvoiceForm? form, CancellationToken ct)
    {
        if (draftId is null || drafts.Find(ctx.WorkspaceId, draftId) is not { } draft)
        {
            return FailIngest(PdfImportErrorCode.DraftExpired, "This draft is gone (it expires after 30 minutes or when the API restarts); upload the PDF again.");
        }
        if (form is null)
        {
            return FailIngest(PdfImportErrorCode.InvalidFields, "Send the invoice's fields.");
        }
        var buyer = await repo.GetBuyerAsync(ctx, ct);
        var validated = Validate(form, buyer);
        if (!validated.IsOk)
        {
            return FailIngest(PdfImportErrorCode.InvalidFields, validated.Error);
        }
        var extracted = validated.Value;
        if (await SameInvoiceOf(ctx, extracted, ct) is { } same)
        {
            return FailIngest(PdfImportErrorCode.Duplicate, $"Invoice {extracted.Number} from this seller is already imported.", same);
        }

        var edited = EditedFields(draft.Invoice, form);
        var source = new DocumentSource("pdf", draft.DocumentSha256, new Dictionary<string, string>
        {
            ["documentSha256"] = draft.DocumentSha256,
            ["extractedBy"] = draft.Provider,
            ["editedFields"] = edited.Count == 0 ? "none" : string.Join(",", edited),
            ["confirmedBy"] = ctx.Actor,
        });
        var result = await ingest.IngestExtractedAsync(ctx, extracted, source, pins: null, ct);
        if (!result.IsOk)
        {
            return FailIngest(Map(result.Error.Code), result.Error.Message, result.Error.ExistingNodeId);
        }
        drafts.Remove(draft.Id);
        return Result<IngestResult, PdfImportError>.Ok(result.Value);
    }

    /// <summary>The fields as an <see cref="ExtractedInvoice"/>, the customer taken from the workspace's buyer; else the first problem.</summary>
    public static Result<ExtractedInvoice, string> Validate(PdfInvoiceForm form, Buyer buyer)
    {
        string? Required(string? value, string name, int max)
        {
            var v = value?.Trim() ?? "";
            return v.Length == 0 ? $"{name} is empty." : v.Length > max ? $"{name} is over {max} characters." : null;
        }

        if (Required(form.SupplierName, "The seller's name", 200) is { } p1) return Bad(p1);
        if (Required(form.SupplierTaxId, "The seller's tax id", 32) is { } p2) return Bad(p2);
        if (Required(form.InvoiceNumber, "The invoice number", 64) is { } p3) return Bad(p3);
        var country = form.SupplierCountry?.Trim().ToUpperInvariant() ?? "";
        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper)) return Bad("The seller's country is not a two-letter code.");
        if (!DateOnly.TryParseExact(form.IssueDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued))
        {
            return Bad("The issue date is not a date (YYYY-MM-DD).");
        }
        var currency = form.Currency?.Trim().ToUpperInvariant() ?? "";
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) return Bad("The currency is not a three-letter code.");
        if (Amount(form.TotalNet) is not { } total || total <= 0m) return Bad("The net total is not a positive amount.");

        var lines = form.Lines ?? [];
        if (lines.Count == 0) return Bad("The invoice has no lines.");
        if (lines.Count > MaxLines) return Bad($"The invoice has over {MaxLines} lines.");
        var extractedLines = new List<ExtractedLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var n = i + 1;
            if (Required(line.Description, $"Line {n}'s description", 500) is { } p4) return Bad(p4);
            if (Amount(line.Quantity) is not { } quantity || quantity <= 0m) return Bad($"Line {n}'s quantity is not a positive number.");
            var unit = line.UnitCode?.Trim().ToUpperInvariant() ?? "";
            if (!UnitCodes.Contains(unit)) return Bad($"Line {n}'s unit is not one of {string.Join(", ", UnitCodes.Order())}.");
            if (Amount(line.Amount) is not { } amount || amount < 0m) return Bad($"Line {n}'s net amount is not an amount.");
            var scheme = line.ClassificationScheme?.Trim().ToUpperInvariant() ?? "";
            var code = line.ClassificationCode?.Trim().Replace(" ", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal) ?? "";
            if (scheme is not ("" or "CPV" or "HS")) return Bad($"Line {n}'s code scheme is not CPV or HS.");
            // A code without its scheme would match CPV and HS prefixes alike, so it counts only with one.
            if (code.Length > 0 && (scheme.Length == 0 || code.Length > 16 || !code.All(char.IsAsciiDigit)))
            {
                return Bad($"Line {n}'s code needs a CPV or HS scheme and digits only.");
            }
            extractedLines.Add(new ExtractedLine(
                n.ToString(CultureInfo.InvariantCulture), line.Description!.Trim(), quantity, unit, amount, null,
                code.Length == 0 ? null : code, code.Length == 0 ? null : scheme));
        }

        return Result<ExtractedInvoice, string>.Ok(new ExtractedInvoice(
            form.InvoiceNumber!.Trim(), issued, currency, total,
            new Party(form.SupplierName!.Trim(), form.SupplierTaxId!.Trim().ToUpperInvariant(), country),
            new Party(buyer.Name, buyer.Cui, buyer.Country),
            extractedLines));
    }

    /// <summary>
    /// An amount as a person may type or the PDF printed it: "9840.00", "9,840.00", "9.840,00", "9840,00", "9 840,00". With
    /// both marks, the last one is the decimal mark. With one kind, it groups thousands when it repeats or has exactly three
    /// digits after it ("1.200" and "9,840" are whole numbers, as Romanian and English invoices print them), and is the
    /// decimal mark otherwise ("12,50", "1.25").
    /// </summary>
    public static decimal? Amount(string? text)
    {
        var t = (text ?? "").Trim().Replace(" ", "", StringComparison.Ordinal).Replace("\u00a0", "", StringComparison.Ordinal);
        if (t.Length == 0 || t.Length > 32)
        {
            return null;
        }
        var lastDot = t.LastIndexOf('.');
        var lastComma = t.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            t = lastComma > lastDot ? t.Replace(".", "", StringComparison.Ordinal).Replace(',', '.') : t.Replace(",", "", StringComparison.Ordinal);
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var mark = lastDot >= 0 ? '.' : ',';
            var at = Math.Max(lastDot, lastComma);
            var thousands = t.Count(c => c == mark) > 1 || t.Length - at - 1 == 3;
            t = thousands ? t.Replace(mark.ToString(), "", StringComparison.Ordinal) : t.Replace(mark, '.');
        }
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>The fields the person changed from what the model copied, by path ("invoiceNumber", "lines.0.amount", "lines").</summary>
    public static IReadOnlyList<string> EditedFields(DraftInvoice draft, PdfInvoiceForm form)
    {
        var edited = new List<string>();
        void Compare(string path, string? was, string? now)
        {
            if (!string.Equals((was ?? "").Trim(), (now ?? "").Trim(), StringComparison.Ordinal))
            {
                edited.Add(path);
            }
        }

        Compare("supplierName", draft.SupplierName, form.SupplierName);
        Compare("supplierTaxId", draft.SupplierTaxId, form.SupplierTaxId);
        Compare("supplierCountry", draft.SupplierCountry, form.SupplierCountry);
        Compare("invoiceNumber", draft.InvoiceNumber, form.InvoiceNumber);
        Compare("issueDate", draft.IssueDate, form.IssueDate);
        Compare("currency", draft.Currency, form.Currency);
        Compare("totalNet", draft.TotalNet, form.TotalNet);
        var lines = form.Lines ?? [];
        if (lines.Count != draft.Lines.Count)
        {
            edited.Add("lines");
        }
        for (var i = 0; i < Math.Min(lines.Count, draft.Lines.Count); i++)
        {
            var (was, now) = (draft.Lines[i], lines[i]);
            Compare($"lines.{i}.description", was.Description, now.Description);
            Compare($"lines.{i}.quantity", was.Quantity, now.Quantity);
            Compare($"lines.{i}.unitCode", was.UnitCode, now.UnitCode);
            Compare($"lines.{i}.amount", was.Amount, now.Amount);
            Compare($"lines.{i}.classificationCode", was.ClassificationCode, now.ClassificationCode);
            Compare($"lines.{i}.classificationScheme", was.ClassificationScheme, now.ClassificationScheme);
        }
        return edited;
    }

    private async Task<Guid?> DuplicateOf(Ctx ctx, string rawHash, CancellationToken ct)
    {
        if (await repo.GetInvoiceByRawHashAsync(ctx, rawHash, ct) is not { } existing)
        {
            return null;
        }
        return (await repo.ListNodesAsync(ctx, ct)).FirstOrDefault(n => n.InvoiceId == existing.Id)?.Id ?? Guid.Empty;
    }

    /// <summary>
    /// An invoice already imported with the same seller tax id and number, whatever its document (the same invoice as UBL
    /// and as PDF). A scan outside the repository's lock: two confirms at the same instant could both pass it, and the raw
    /// hashes differ, so this is a guard against a person's mistake, not a guarantee.
    /// </summary>
    private async Task<Guid?> SameInvoiceOf(Ctx ctx, ExtractedInvoice extracted, CancellationToken ct)
    {
        foreach (var node in await repo.ListNodesAsync(ctx, ct))
        {
            var invoice = await repo.GetInvoiceAsync(ctx, node.InvoiceId, ct);
            if (invoice is not null
                && string.Equals(invoice.VendorCui, extracted.Supplier.TaxId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(invoice.Extracted.Number, extracted.Number, StringComparison.OrdinalIgnoreCase))
            {
                return node.Id;
            }
        }
        return null;
    }

    private static PdfImportErrorCode Map(IngestErrorCode code) => code switch
    {
        IngestErrorCode.Duplicate => PdfImportErrorCode.Duplicate,
        _ => PdfImportErrorCode.InvalidFields,
    };

    private static Result<ExtractedInvoice, string> Bad(string problem) => Result<ExtractedInvoice, string>.Fail(problem);

    private static Result<PdfDraftView, PdfImportError> Fail(PdfImportErrorCode code, string message, Guid? node = null) =>
        Result<PdfDraftView, PdfImportError>.Fail(new PdfImportError(code, message, node));

    private static Result<IngestResult, PdfImportError> FailIngest(PdfImportErrorCode code, string message, Guid? node = null) =>
        Result<IngestResult, PdfImportError>.Fail(new PdfImportError(code, message, node));
}
