using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Ubl;

public enum ParseErrorCode { TooLarge, DoctypeForbidden, NotUblInvoice, Schema }

public sealed record ParseError(ParseErrorCode Code, string Message);

/// <summary>One implementation per input format; all of them produce the canonical <see cref="ExtractedInvoice"/>.</summary>
public interface IInvoiceParser
{
    string Name { get; }

    bool Detect(string input);

    Result<ExtractedInvoice, ParseError> Parse(string input);
}
