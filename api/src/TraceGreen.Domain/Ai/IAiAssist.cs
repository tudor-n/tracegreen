using TraceGreen.Domain.Ports;

namespace TraceGreen.Domain.Ai;

/// <summary>
/// One fact the chat model may cite (F13): its id, what it is about, and its value as the prompt shows it. The model
/// never writes the value; it writes <c>{{id}}</c> and the API fills in the value from the data.
/// </summary>
public sealed record ChatFactInput(string Id, string About, string Value);

/// <summary>The buyer's question, up to three earlier questions (never earlier answers) and the facts it may cite.</summary>
public sealed record ChatInput(string Question, IReadOnlyList<string> History, IReadOnlyList<ChatFactInput> Facts);

/// <summary>
/// The model's answer: a sentence with <c>{{id}}</c> placeholders and the ids it cites, or <see cref="Answerable"/>
/// false when the facts do not answer the question. <see cref="Provider"/> is the model id, or "stub".
/// </summary>
public sealed record ChatOutcome(bool Answerable, string Text, IReadOnlyList<string> FactIds, string Provider);

/// <summary>One invoice line as a model copied it from a PDF (F9); every value a string as printed, empty when not shown.</summary>
public sealed record DraftLine(string Description, string Quantity, string UnitCode, string Amount, string ClassificationCode, string ClassificationScheme);

/// <summary>The fields a model copied from a PDF invoice's text. Nothing here is trusted until a person confirms it.</summary>
public sealed record DraftInvoice(
    string SupplierName,
    string SupplierTaxId,
    string SupplierCountry,
    string InvoiceNumber,
    string IssueDate,
    string Currency,
    string TotalNet,
    IReadOnlyList<DraftLine> Lines);

/// <summary>Why a PDF could not be extracted.</summary>
public enum ExtractFailure { NoText, Unreadable, TooLarge, Unavailable }

/// <summary>
/// The draft, and by field path ("invoiceNumber", "lines.0.amount") whether its value is in the PDF's text; or the failure.
/// </summary>
public sealed record ExtractOutcome(DraftInvoice? Invoice, IReadOnlyDictionary<string, bool> Found, string Provider, int Pages, ExtractFailure? Failure)
{
    public static ExtractOutcome Failed(ExtractFailure failure) => new(null, new Dictionary<string, bool>(), "stub", 0, failure);
}

/// <summary>
/// The AI service's longer tasks, which no import waits on: the data chat (F13) and PDF extraction (F9).
/// Implementations may throw on transport errors; callers fall back.
/// </summary>
public interface IAiAssist
{
    Task<ChatOutcome> ChatAsync(ChatInput input, CancellationToken ct);

    /// <summary>The text layer of a PDF invoice and the fields a model copied from it (F9); never imports anything.</summary>
    Task<ExtractOutcome> ExtractPdfAsync(byte[] pdf, CancellationToken ct);

    Task<HealthStatus> HealthAsync(CancellationToken ct);
}
