using TraceGreen.Domain.Trust;

namespace TraceGreen.Tests.Domain.Trust;

public class TaxIdsTests
{
    [Theory]
    [InlineData("RO99999992")] // the seed buyer, checked at ANAF
    [InlineData("RO99999984")]
    [InlineData("99999968")]
    [InlineData("RO 9999 9976")]
    public void RomanianCuisWithTheirCheckDigitPass(string id) => Assert.Equal(TaxIdCheck.Valid, TaxIds.Check(id, "RO"));

    [Theory]
    [InlineData("RO99999993")]
    [InlineData("RO1")]
    [InlineData("ROABC")]
    public void RomanianCuisWithoutItFail(string id) => Assert.Equal(TaxIdCheck.Invalid, TaxIds.Check(id, "RO"));

    [Fact]
    public void AChineseCreditCodeChecksItsLastCharacter()
    {
        Assert.Equal(TaxIdCheck.Valid, TaxIds.Check("91350100M000100Y43", "CN"));
        Assert.Equal(TaxIdCheck.Invalid, TaxIds.Check("91350100M000100Y44", "CN"));
        // The fictional N1 seller's id has 16 characters, not 18.
        Assert.Equal(TaxIdCheck.Invalid, TaxIds.Check("CN91440300MA5K7XQ2", "CN"));
    }

    [Fact]
    public void ATaiwaneseBusinessNumberChecksItsDigitSums()
    {
        Assert.Equal(TaxIdCheck.Valid, TaxIds.Check("12345675", "TW")); // a 7 in seventh place: 39 or 40
        Assert.Equal(TaxIdCheck.Valid, TaxIds.Check("04595252", "TW"));
        Assert.Equal(TaxIdCheck.Invalid, TaxIds.Check("TW24567890", "TW")); // the fictional N2 seller's
    }

    [Fact]
    public void OtherCountriesAreNotChecked() => Assert.Equal(TaxIdCheck.Unchecked, TaxIds.Check("DE123456789", "DE"));

    [Fact]
    public void NormalisingDropsSpacesCaseAndTheOwnCountryPrefix()
    {
        Assert.Equal("99999984", TaxIds.Normalize(" ro 9999 9984", "RO"));
        Assert.Equal("DE123", TaxIds.Normalize("de123", "RO"));
        Assert.Equal("RO", TaxIds.Normalize("RO", "RO"));
    }
}
