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

    /// <summary>Invoice code first, then the pre-computed cache; otherwise UNCLASSIFIED (the live AI is consulted by the service).</summary>
    public static (string CategoryId, double? Confidence, ClassificationSource Source) Offline(ExtractedLine line, SeedBundle bundle)
    {
        if (Match(line, bundle.ClassificationMap) is { } mapped)
        {
            return (mapped.CategoryId, 1.0, ClassificationSource.Invoice);
        }
        if (bundle.AiCache.TryGetValue(CacheKey.Compute(line.Description), out var cached) && bundle.AllowList.Contains(cached.CategoryId))
        {
            return (cached.CategoryId, cached.Confidence, ClassificationSource.Cache);
        }
        return (Ids.Unclassified, null, ClassificationSource.Stub);
    }
}
