using System.Globalization;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Carbon;

/// <summary>An invoice amount in the spend factors' currency and price year, with the rows used to get there.</summary>
public sealed record ConvertedSpend(decimal Amount, IReadOnlyList<FactorLookup> Steps);

/// <summary>
/// Turns an EUR invoice amount into the currency and price year of the spend factors (D1: US EPA v1.3, 2022 USD):
/// EUR → USD at the ECB reference rate of the invoice date (the last published rate on or before it), then to the
/// factor price year with the CPI-U annual averages (the latest full year when the invoice year has none yet).
/// Every step is returned as a row, so the "i" icon shows the rate and the index next to the factor.
/// </summary>
public sealed class SpendConversion
{
    /// <summary>Rounding of the price-index ratio; the rounded ratio is both used and shown.</summary>
    public const int RatioDecimals = 4;

    private readonly SpendConversionSpec _spec;
    private readonly (DateOnly Date, decimal Rate)[] _rates;
    private readonly SortedDictionary<int, decimal> _index;

    public SpendConversion(SpendConversionSpec spec)
    {
        _spec = spec;
        _rates = spec.ExchangeRate.Rates
            .Select(r => (DateOnly.ParseExact(r.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), r.Value))
            .OrderBy(r => r.Item1)
            .ToArray();
        _index = new SortedDictionary<int, decimal>(
            spec.PriceIndex.AnnualAverages.ToDictionary(a => int.Parse(a.Key, CultureInfo.InvariantCulture), a => a.Value));
        if (_rates.Length == 0) throw new SeedValidationException(["spend-conversion.json: no exchange rates"]);
        if (!_index.ContainsKey(spec.FactorPriceYear))
        {
            throw new SeedValidationException([$"spend-conversion.json: no price index for the factor price year {spec.FactorPriceYear}"]);
        }
    }

    public string FactorCurrency => _spec.FactorCurrency;

    public ConvertedSpend FromEur(decimal amountEur, DateOnly issueDate)
    {
        var fx = ExchangeRate(issueDate);
        var price = PriceAdjustment(issueDate.Year);
        return new ConvertedSpend(amountEur * fx.Value * price.Value, [fx, price]);
    }

    /// <summary>The last rate published on or before the date; the first one for a date before the table starts.</summary>
    public FactorLookup ExchangeRate(DateOnly date)
    {
        var (rateDate, rate) = _rates.LastOrDefault(r => r.Date <= date, _rates[0]);
        var label = rateDate == date
            ? $"US dollar/Euro reference rate, {rateDate:yyyy-MM-dd}"
            : $"US dollar/Euro reference rate, {rateDate:yyyy-MM-dd} (last published rate for {date:yyyy-MM-dd})";
        return new FactorLookup(rate, _spec.ExchangeRate.Unit, _spec.ExchangeRate.Source, rateDate.Year, label);
    }

    /// <summary>CPI-U(factor price year) ÷ CPI-U(invoice year), the invoice year clamped to the years published.</summary>
    public FactorLookup PriceAdjustment(int invoiceYear)
    {
        var year = Math.Clamp(invoiceYear, _index.Keys.First(), _index.Keys.Last());
        var basePrice = _index[_spec.FactorPriceYear];
        var invoicePrice = _index[year];
        var ratio = Math.Round(basePrice / invoicePrice, RatioDecimals);
        var label = $"CPI-U annual average {_spec.FactorPriceYear} / {year} ({basePrice.ToString(CultureInfo.InvariantCulture)} / {invoicePrice.ToString(CultureInfo.InvariantCulture)})";
        if (year != invoiceYear) label += $", {year} standing in for {invoiceYear}";
        return new FactorLookup(ratio, $"USD {_spec.FactorPriceYear} per USD {year}", _spec.PriceIndex.Source, year, label);
    }
}
