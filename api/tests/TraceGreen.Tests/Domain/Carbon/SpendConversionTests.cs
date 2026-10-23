using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Domain.Carbon;

public class SpendConversionTests
{
    private static readonly SpendConversion Seeded = new(SeedLoader.Load(TestPaths.SeedDir).SpendConversion);

    private static SpendConversion Small() => new(new SpendConversionSpec(
        "USD",
        2022,
        new ExchangeRateSpec("ECB test", "USD/EUR", "url", "2026-10-23", new Dictionary<string, decimal>
        {
            ["2026-10-01"] = 1.10m,
            ["2026-10-02"] = 1.20m,
            ["2026-10-05"] = 1.30m,
        }),
        new PriceIndexSpec("CPI test", "url", "2026-10-23", new Dictionary<string, decimal>
        {
            ["2022"] = 300m,
            ["2024"] = 320m,
            ["2025"] = 330m,
        })));

    [Fact]
    public void UsesTheRateOfTheInvoiceDate()
    {
        var fx = Small().ExchangeRate(new DateOnly(2026, 10, 2));

        Assert.Equal(1.20m, fx.Value);
        Assert.Equal("US dollar/Euro reference rate, 2026-10-02", fx.RowLabel);
        Assert.Equal(2026, fx.Year);
    }

    [Fact]
    public void UsesTheLastPublishedRateForAWeekendOrADateAfterTheTable()
    {
        Assert.Equal(1.20m, Small().ExchangeRate(new DateOnly(2026, 10, 4)).Value);
        var late = Small().ExchangeRate(new DateOnly(2026, 10, 28));
        Assert.Equal(1.30m, late.Value);
        Assert.Contains("2026-10-05 (last published rate for 2026-10-28)", late.RowLabel);
    }

    [Fact]
    public void UsesTheFirstRateForADateBeforeTheTable()
    {
        Assert.Equal(1.10m, Small().ExchangeRate(new DateOnly(2025, 1, 1)).Value);
    }

    [Fact]
    public void AdjustsToTheFactorPriceYearWithTheInvoiceYearIndex()
    {
        var price = Small().PriceAdjustment(2024);

        Assert.Equal(Math.Round(300m / 320m, SpendConversion.RatioDecimals), price.Value);
        Assert.Equal("USD 2022 per USD 2024", price.Unit);
        Assert.Equal("CPI-U annual average 2022 / 2024 (300 / 320)", price.RowLabel);
    }

    [Fact]
    public void ClampsTheInvoiceYearToThePublishedYearsAndSaysSo()
    {
        var price = Small().PriceAdjustment(2026);

        Assert.Equal(Math.Round(300m / 330m, SpendConversion.RatioDecimals), price.Value);
        Assert.Equal(2025, price.Year);
        Assert.EndsWith("2025 standing in for 2026", price.RowLabel);
        Assert.Equal(1m, Small().PriceAdjustment(2020).Value);
    }

    [Fact]
    public void FromEurMultipliesAmountRateAndRatio()
    {
        var converted = Small().FromEur(1000m, new DateOnly(2026, 10, 5));

        Assert.Equal(1000m * 1.30m * Math.Round(300m / 330m, 4), converted.Amount);
        Assert.Equal(2, converted.Steps.Count);
    }

    [Fact]
    public void SeedTableMatchesTheSourcesCheckedOn20261023()
    {
        Assert.Equal(1.1298m, Seeded.ExchangeRate(new DateOnly(2026, 10, 1)).Value);
        Assert.Equal(0.9090m, Seeded.PriceAdjustment(2026).Value);
        Assert.Equal("USD", Seeded.FactorCurrency);
    }

    [Fact]
    public void RefusesATableWithoutTheFactorPriceYear()
    {
        var spec = new SpendConversionSpec(
            "USD", 2021,
            new ExchangeRateSpec("s", "USD/EUR", "u", "r", new Dictionary<string, decimal> { ["2026-10-01"] = 1m }),
            new PriceIndexSpec("s", "u", "r", new Dictionary<string, decimal> { ["2022"] = 300m }));

        Assert.Throws<SeedValidationException>(() => new SpendConversion(spec));
    }
}
