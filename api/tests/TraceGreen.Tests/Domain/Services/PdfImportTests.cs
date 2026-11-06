using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Services;

namespace TraceGreen.Tests.Domain.Services;

public class PdfImportTests
{
    private static readonly Buyer Buyer = new("Transilvania Electro-Assembly SRL", "RO99999992", "RO", "Sibiu");

    private static PdfInvoiceForm Form(params DraftLine[] lines) => new(
        "Zincora Turnatorie SRL", "ro99999968", "ro", "ZT-1", "2026-10-22", "eur", "9.840,00",
        lines.Length == 0 ? [new DraftLine("Housings", "1.200", "h87", "9.840,00", "7907 00", "hs")] : lines);

    [Theory]
    [InlineData("9840.00", 9840.00)]
    [InlineData("9,840.00", 9840.00)]
    [InlineData("9.840,00", 9840.00)]
    [InlineData("9840,00", 9840.00)]
    [InlineData("9 840,00", 9840.00)]
    [InlineData("1.200", 1200)]
    [InlineData("9,840", 9840)]
    [InlineData("12,50", 12.50)]
    [InlineData("1.25", 1.25)]
    [InlineData("1.234.567,89", 1234567.89)]
    public void AmountsReadInEitherConvention(string text, double expected) =>
        Assert.Equal((decimal)expected, PdfImportService.Amount(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    public void NotAnAmountIsNull(string? text) => Assert.Null(PdfImportService.Amount(text));

    [Fact]
    public void ValidFieldsBecomeAnExtractedInvoiceWithTheBuyerAsCustomer()
    {
        var result = PdfImportService.Validate(Form(), Buyer);

        Assert.True(result.IsOk, result.IsOk ? null : result.Error);
        var invoice = result.Value;
        Assert.Equal(("EUR", 9840.00m), (invoice.Currency, invoice.Total));
        Assert.Equal(new Party("Zincora Turnatorie SRL", "RO99999968", "RO"), invoice.Supplier);
        Assert.Equal(new Party(Buyer.Name, Buyer.Cui, Buyer.Country), invoice.Customer);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((1200m, "H87", 9840.00m, "790700", "HS"), (line.Quantity, line.UnitCode, line.Amount, line.ClassificationCode, line.ClassificationListId));
    }

    [Fact]
    public void ACodeWithoutItsSchemeIsRefused()
    {
        var result = PdfImportService.Validate(Form(new DraftLine("Housings", "1", "H87", "10", "790700", "")), Buyer);

        Assert.False(result.IsOk);
        Assert.Contains("CPV or HS", result.Error);
    }

    [Fact]
    public void EditedFieldsNameWhatThePersonChanged()
    {
        var draft = new DraftInvoice("A", "RO1", "RO", "N1", "2026-10-22", "EUR", "10", [new DraftLine("x", "1", "H87", "10", "", "")]);
        var form = new PdfInvoiceForm("A", "RO1", "RO", "N2", "2026-10-22", "EUR", "10", [new DraftLine("x", "2", "H87", "10", "", ""), new DraftLine("y", "1", "H87", "1", "", "")]);

        Assert.Equal(["invoiceNumber", "lines", "lines.0.quantity"], PdfImportService.EditedFields(draft, form));
    }
}
