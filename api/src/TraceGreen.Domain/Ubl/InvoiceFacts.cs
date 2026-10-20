using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Ubl;

/// <summary>Facts derived from invoice lines before any AI runs.</summary>
public static class InvoiceFacts
{
    /// <summary>Total mass in tonnes from lines whose quantity carries a mass unit code (UN/ECE Rec. 20), or null when none does.</summary>
    public static decimal? MassTFromLines(IEnumerable<ExtractedLine> lines)
    {
        decimal? total = null;
        foreach (var line in lines)
        {
            var tonnes = line.UnitCode.ToUpperInvariant() switch
            {
                "KGM" => line.Quantity / 1000m,
                "TNE" => line.Quantity,
                "GRM" => line.Quantity / 1_000_000m,
                _ => (decimal?)null,
            };
            if (tonnes is { } t)
            {
                total = (total ?? 0m) + t;
            }
        }
        return total;
    }
}
