using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Ubl;

/// <summary>
/// Reads an EN 16931 UBL 2.1 invoice (RO_CIUS included) by local element names, so any namespace prefix works.
/// DTDs are rejected before parsing; the input is treated as untrusted text throughout.
/// </summary>
public sealed class UblInvoiceParser : IInvoiceParser
{
    public const int MaxChars = 2 * 1024 * 1024;

    /// <summary>UBL invoices nest about ten levels deep. Deeper input is refused before a tree is built: building and
    /// reading a deeply nested tree costs quadratic time and recurses until the process overflows its stack.</summary>
    public const int MaxDepth = 64;

    public string Name => "ubl-2.1";

    public bool Detect(string input)
    {
        var head = input.AsSpan(0, Math.Min(input.Length, 4096)).TrimStart();
        return head.StartsWith("<") && head.Contains("Invoice", StringComparison.Ordinal);
    }

    public Result<ExtractedInvoice, ParseError> Parse(string input)
    {
        if (input.Length > MaxChars)
        {
            return Fail(ParseErrorCode.TooLarge, $"Input exceeds {MaxChars} characters.");
        }
        if (input.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(ParseErrorCode.DoctypeForbidden, "Document type definitions are not accepted.");
        }

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            if (ExceedsDepth(input, settings))
            {
                return Fail(ParseErrorCode.Schema, $"Elements are nested deeper than {MaxDepth} levels.");
            }
            using var reader = XmlReader.Create(new StringReader(input), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            return Fail(ParseErrorCode.Schema, "Malformed XML: " + ex.Message);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Invoice")
        {
            return Fail(ParseErrorCode.NotUblInvoice, "Root element is not a UBL Invoice.");
        }

        try
        {
            return Result<ExtractedInvoice, ParseError>.Ok(Extract(root));
        }
        catch (SchemaException ex)
        {
            return Fail(ParseErrorCode.Schema, ex.Message);
        }
    }

    /// <summary>A streaming pass that stops at the first element deeper than <see cref="MaxDepth"/>; it never builds a tree.</summary>
    private static bool ExceedsDepth(string input, XmlReaderSettings settings)
    {
        using var reader = XmlReader.Create(new StringReader(input), settings);
        while (reader.Read())
        {
            if (reader.Depth > MaxDepth)
            {
                return true;
            }
        }
        return false;
    }

    private static ExtractedInvoice Extract(XElement root)
    {
        var number = Require(root, "cbc:ID", "ID");
        var issueDate = DateOnly.TryParseExact(Require(root, "cbc:IssueDate", "IssueDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new SchemaException("cbc:IssueDate is not an ISO date");
        var currency = Require(root, "cbc:DocumentCurrencyCode", "DocumentCurrencyCode");

        var supplier = Party(Path(root, "AccountingSupplierParty", "Party") ?? throw new SchemaException("missing cac:AccountingSupplierParty"));
        var customer = Party(Path(root, "AccountingCustomerParty", "Party") ?? throw new SchemaException("missing cac:AccountingCustomerParty"));

        var lines = root.Elements().Where(e => e.Name.LocalName == "InvoiceLine").Select(Line).ToList();
        if (lines.Count == 0)
        {
            throw new SchemaException("no cac:InvoiceLine");
        }

        var totals = Child(root, "LegalMonetaryTotal");
        var totalText = Text(Child(totals, "TaxExclusiveAmount")) ?? Text(Child(totals, "LineExtensionAmount"));
        var total = totalText is null ? lines.Sum(l => l.Amount) : Decimal(totalText, "LegalMonetaryTotal");

        return new ExtractedInvoice(number, issueDate, currency, total, supplier, customer, lines);
    }

    private static Party Party(XElement party)
    {
        var name = Text(Path(party, "PartyLegalEntity", "RegistrationName"))
            ?? Text(Path(party, "PartyName", "Name"))
            ?? throw new SchemaException("party without a name");
        var taxId = Text(Path(party, "PartyTaxScheme", "CompanyID"))
            ?? Text(Path(party, "PartyLegalEntity", "CompanyID"))
            ?? "";
        var country = Text(Path(party, "PostalAddress", "Country", "IdentificationCode")) ?? "";
        return new Party(name, taxId, country);
    }

    private static ExtractedLine Line(XElement line, int index)
    {
        var quantity = Child(line, "InvoicedQuantity");
        var item = Child(line, "Item");
        var classification = Path(item, "CommodityClassification", "ItemClassificationCode");
        var description = Text(Child(item, "Name")) ?? "";
        if (Text(Child(item, "Description")) is { } more)
        {
            description = description.Length == 0 ? more : description + " - " + more;
        }

        return new ExtractedLine(
            Id: Text(Child(line, "ID")) ?? (index + 1).ToString(CultureInfo.InvariantCulture),
            Description: description,
            Quantity: Decimal(Text(quantity) ?? throw new SchemaException($"line {index + 1}: missing cbc:InvoicedQuantity"), "InvoicedQuantity"),
            UnitCode: quantity?.Attribute("unitCode")?.Value.Trim() ?? "",
            Amount: Decimal(Text(Child(line, "LineExtensionAmount")) ?? throw new SchemaException($"line {index + 1}: missing cbc:LineExtensionAmount"), "LineExtensionAmount"),
            OriginCountry: Text(Path(item, "OriginCountry", "IdentificationCode")),
            ClassificationCode: Text(classification),
            ClassificationListId: ListId(classification?.Attribute("listID")?.Value.Trim()));
    }

    /// <summary>ANAF accepts CPV codes only under the UNTDID 7143 scheme identifier STI (it rejects "CPV" by BR-CL-13);
    /// the rest of the system knows them as CPV.</summary>
    private static string? ListId(string? listId) =>
        string.Equals(listId, "STI", StringComparison.OrdinalIgnoreCase) ? "CPV" : listId;

    private static XElement? Child(XElement? element, string localName) =>
        element?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static XElement? Path(XElement? element, params string[] localNames)
    {
        foreach (var name in localNames)
        {
            element = Child(element, name);
        }
        return element;
    }

    private static string? Text(XElement? element)
    {
        var value = element?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string Require(XElement root, string label, string localName) =>
        Text(Child(root, localName)) ?? throw new SchemaException($"missing {label}");

    private static decimal Decimal(string text, string label) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new SchemaException($"{label} is not a number: {text}");

    private static Result<ExtractedInvoice, ParseError> Fail(ParseErrorCode code, string message) =>
        Result<ExtractedInvoice, ParseError>.Fail(new ParseError(code, message));

    private sealed class SchemaException(string message) : Exception(message);
}
