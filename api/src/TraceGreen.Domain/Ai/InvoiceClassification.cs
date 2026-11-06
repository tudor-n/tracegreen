using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Ai;

/// <summary>Classification that needs no AI: the invoice's own item classification code (BT-158) matched by prefix.</summary>
public static class InvoiceClassification
{
    public static ClassificationMapEntry? Match(ExtractedLine line, IReadOnlyList<ClassificationMapEntry> map)
    {
        if (string.IsNullOrWhiteSpace(line.ClassificationCode))
        {
            return null;
        }
        return map
            .Where(m => line.ClassificationListId is null || string.Equals(m.ListId, line.ClassificationListId, StringComparison.OrdinalIgnoreCase))
            .Where(m => line.ClassificationCode.StartsWith(m.Prefix, StringComparison.Ordinal))
            .OrderByDescending(m => m.Prefix.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Invoice code first, then a category the buyer chose for the same description (<paramref name="buyerCategories"/>, by
    /// <see cref="CacheKey"/>), then the pre-computed cache; otherwise UNCLASSIFIED (the live AI is consulted by the service).
    /// </summary>
    public static (string CategoryId, double? Confidence, ClassificationSource Source, string Rationale) Offline(
        ExtractedLine line, SeedBundle bundle, IReadOnlyDictionary<string, string>? buyerCategories = null)
    {
        if (Match(line, bundle.ClassificationMap) is { } mapped)
        {
            return (mapped.CategoryId, 1.0, ClassificationSource.Invoice, $"Item classification code {line.ClassificationCode} ({mapped.ListId})");
        }
        var key = CacheKey.Compute(line.Description);
        if (buyerCategories is not null && buyerCategories.TryGetValue(key, out var chosen) && bundle.AllowList.Contains(chosen))
        {
            return (chosen, null, ClassificationSource.Buyer, "The buyer's review of the same description");
        }
        if (bundle.AiCache.TryGetValue(key, out var cached) && bundle.AllowList.Contains(cached.CategoryId))
        {
            return (cached.CategoryId, cached.Confidence, ClassificationSource.Cache, cached.Rationale);
        }
        return (Ids.Unclassified, null, ClassificationSource.Stub, "No classification available");
    }
}
