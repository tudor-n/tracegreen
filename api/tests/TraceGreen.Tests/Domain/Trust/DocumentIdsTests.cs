using TraceGreen.Domain.Model;
using TraceGreen.Domain.Trust;

namespace TraceGreen.Tests.Domain.Trust;

public class DocumentIdsTests
{
    [Theory]
    [InlineData("CSQU3054383")] // ISO 6346's worked example
    [InlineData("MSKU9070323")]
    public void AContainerNumberWithItsCheckDigitPasses(string id) =>
        Assert.Null(DocumentIds.Problem(ShippingDocType.ContainerNumber, id));

    [Fact]
    public void AContainerNumberWithAnotherCheckDigitFails() =>
        Assert.Equal("The container number's check digit does not match.", DocumentIds.Problem(ShippingDocType.ContainerNumber, "CSQU3054384"));

    [Theory]
    [InlineData("CSQ3054383")]
    [InlineData("CSQA3054383")] // the 4th letter must be U, J or Z
    [InlineData("CSQU305438")]
    public void AContainerNumberOfAnotherShapeFails(string id) =>
        Assert.StartsWith("A container number is", DocumentIds.Problem(ShippingDocType.ContainerNumber, id));

    [Fact]
    public void AnAirWaybillChecksItsSerialModSeven()
    {
        // 1234567 mod 7 = 5.
        Assert.Null(DocumentIds.Problem(ShippingDocType.AirWaybill, "02012345675"));
        Assert.Equal("The air waybill number's check digit does not match.", DocumentIds.Problem(ShippingDocType.AirWaybill, "02012345676"));
        Assert.StartsWith("An air waybill number is", DocumentIds.Problem(ShippingDocType.AirWaybill, "0201234567"));
    }

    [Fact]
    public void NumbersAreNormalisedBeforeTheCheck()
    {
        Assert.Equal("CSQU3054383", DocumentIds.Normalize(ShippingDocType.ContainerNumber, " csqu 305438-3 "));
        Assert.Equal("02012345675", DocumentIds.Normalize(ShippingDocType.AirWaybill, "020-1234 5675"));
        Assert.Equal("MEDU-1234/56", DocumentIds.Normalize(ShippingDocType.BillOfLading, "medu-1234/56"));
    }

    [Theory]
    [InlineData(ShippingDocType.ContainerNumber, TransportMode.Sea, true)]
    [InlineData(ShippingDocType.ContainerNumber, TransportMode.Rail, true)]
    [InlineData(ShippingDocType.ContainerNumber, TransportMode.Air, false)]
    [InlineData(ShippingDocType.AirWaybill, TransportMode.Air, true)]
    [InlineData(ShippingDocType.AirWaybill, TransportMode.Sea, false)]
    [InlineData(ShippingDocType.BillOfLading, TransportMode.Sea, true)]
    [InlineData(ShippingDocType.BillOfLading, TransportMode.Rail, false)]
    [InlineData(ShippingDocType.Cmr, TransportMode.Road, true)]
    [InlineData(ShippingDocType.ETransportUit, TransportMode.Road, true)]
    [InlineData(ShippingDocType.RailConsignment, TransportMode.Rail, true)]
    [InlineData(ShippingDocType.RailConsignment, TransportMode.Road, false)]
    public void EachDocumentGoesWithItsModes(ShippingDocType type, TransportMode mode, bool fits) =>
        Assert.Equal(fits, DocumentIds.Fits(type, mode));

    [Fact]
    public void FreeFormNumbersAreCheckedForShapeOnly()
    {
        Assert.Null(DocumentIds.Problem(ShippingDocType.Cmr, "RO-4471"));
        Assert.NotNull(DocumentIds.Problem(ShippingDocType.Cmr, "RO4"));
        Assert.NotNull(DocumentIds.Problem(ShippingDocType.BillOfLading, "MEDU 123"));
        Assert.Null(DocumentIds.CertificateProblem("GO-RO-2026-000123"));
        Assert.NotNull(DocumentIds.CertificateProblem("abc"));
        Assert.NotNull(DocumentIds.CertificateProblem("cert <script>"));
    }
}
