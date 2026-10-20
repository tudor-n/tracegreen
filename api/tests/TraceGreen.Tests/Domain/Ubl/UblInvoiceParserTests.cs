using TraceGreen.Domain.Model;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Tests.Domain.Ubl;

public class UblInvoiceParserTests
{
    private static readonly UblInvoiceParser Parser = new();
    private static string N1 => File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml"));

    private static ExtractedInvoice ParseOk(string xml)
    {
        var result = Parser.Parse(xml);
        Assert.True(result.IsOk, result.IsOk ? "" : result.Error.Message);
        return result.Value;
    }

    [Fact]
    public void ParsesN1WithSupplierLineMassOriginAndClassification()
    {
        var invoice = ParseOk(N1);

        Assert.Equal("SDC-2026-1028", invoice.Number);
        Assert.Equal(new DateOnly(2026, 10, 28), invoice.IssueDate);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(48000.00m, invoice.Total);
        Assert.Equal("Shenzhen Diecast Co.", invoice.Supplier.Name);
        Assert.Equal("CN91440300MA5K7XQ2", invoice.Supplier.TaxId);
        Assert.Equal("CN", invoice.Supplier.Country);
        Assert.Equal("RO99999999", invoice.Customer.TaxId);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal("1", line.Id);
        Assert.Equal("Zinc die-cast sensor housings, type ZH-220", line.Description);
        Assert.Equal(2300m, line.Quantity);
        Assert.Equal("KGM", line.UnitCode);
        Assert.Equal(48000.00m, line.Amount);
        Assert.Equal("CN", line.OriginCountry);
        Assert.Equal("44410000", line.ClassificationCode);
        Assert.Equal("CPV", line.ClassificationListId);
        Assert.Equal(2.3m, InvoiceFacts.MassTFromLines(invoice.Lines));
    }

    [Fact]
    public void PrefixedAndUnprefixedNamespaceVariantsParseIdentically()
    {
        var standard = ParseOk(N1);
        var renamedPrefixes = N1
            .Replace("xmlns:cac=", "xmlns:ns2=").Replace("xmlns:cbc=", "xmlns:ns3=")
            .Replace("<cac:", "<ns2:").Replace("</cac:", "</ns2:")
            .Replace("<cbc:", "<ns3:").Replace("</cbc:", "</ns3:");
        var noPrefixes = N1
            .Replace(" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\"", "")
            .Replace(" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\"", "")
            .Replace("<cac:", "<").Replace("</cac:", "</")
            .Replace("<cbc:", "<").Replace("</cbc:", "</");

        Assert.Equal(Hashing.InvoiceHash(standard), Hashing.InvoiceHash(ParseOk(renamedPrefixes)));
        Assert.Equal(Hashing.InvoiceHash(standard), Hashing.InvoiceHash(ParseOk(noPrefixes)));
    }

    [Fact]
    public void LineWithoutKgmUnitGivesNullMass()
    {
        var invoice = ParseOk(N1.Replace("unitCode=\"KGM\"", "unitCode=\"C62\""));

        Assert.Equal("C62", invoice.Lines[0].UnitCode);
        Assert.Null(InvoiceFacts.MassTFromLines(invoice.Lines));
    }

    [Fact]
    public void RejectsDoctype()
    {
        var result = Parser.Parse("<!DOCTYPE x [<!ENTITY e \"x\">]><Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"/>");

        Assert.False(result.IsOk);
        Assert.Equal(ParseErrorCode.DoctypeForbidden, result.Error.Code);
    }

    [Fact]
    public void RejectsInputOverTwoMegabytes()
    {
        var result = Parser.Parse(new string('x', 2 * 1024 * 1024 + 1));

        Assert.False(result.IsOk);
        Assert.Equal(ParseErrorCode.TooLarge, result.Error.Code);
    }

    [Fact]
    public void RejectsNonInvoiceRoot()
    {
        var result = Parser.Parse("<CreditNote xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2\"><ID>1</ID></CreditNote>");

        Assert.False(result.IsOk);
        Assert.Equal(ParseErrorCode.NotUblInvoice, result.Error.Code);
    }

    [Fact]
    public void RejectsMalformedXmlAndMissingFields()
    {
        Assert.Equal(ParseErrorCode.Schema, Parser.Parse("<Invoice><ID>").Error.Code);
        Assert.Equal(ParseErrorCode.Schema, Parser.Parse("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"><ID>X</ID></Invoice>").Error.Code);
    }

    [Fact]
    public void AdversarialDescriptionIsPreservedVerbatimAsData()
    {
        var invoice = ParseOk(File.ReadAllText(TestPaths.Invoice("adversarial.xml")));

        Assert.Contains("IGNORE PREVIOUS INSTRUCTIONS", invoice.Lines[0].Description);
    }

    [Fact]
    public void DetectRecognisesUblInvoices()
    {
        Assert.True(Parser.Detect(N1));
        Assert.False(Parser.Detect("{\"not\":\"xml\"}"));
        Assert.Equal("ubl-2.1", Parser.Name);
    }

    [Fact]
    public void AllSeedInvoicesParse()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.SeedDir, "invoices"), "*.xml"))
        {
            var result = Parser.Parse(File.ReadAllText(file));
            Assert.True(result.IsOk, Path.GetFileName(file));
            Assert.NotNull(InvoiceFacts.MassTFromLines(result.Value.Lines));
        }
    }
}
