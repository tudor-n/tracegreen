using System.Text.Json;
using System.Text.Json.Serialization;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Seed;

public sealed class SeedValidationException(IReadOnlyList<string> violations)
    : Exception("Seed bundle is invalid:\n - " + string.Join("\n - ", violations))
{
    public IReadOnlyList<string> Violations { get; } = violations;
}

/// <summary>Reads and cross-validates the shared <c>seed/</c> directory.</summary>
public static class SeedLoader
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static SeedBundle Load(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "factors.json")))
        {
            throw new DirectoryNotFoundException($"Seed directory not found or incomplete: {directory}");
        }

        var buyer = Read<Buyer>(directory, "buyer.json");
        var categories = Read<List<Category>>(directory, "categories.json");
        var factors = Read<List<Factor>>(directory, "factors.json");
        var lanes = Read<List<LaneDto>>(directory, "lanes.json").Select(ToLane).ToList();
        var alternatives = Read<List<Alternative>>(directory, "alternatives.json");
        var classificationMap = Read<List<ClassificationMapEntry>>(directory, "classification-map.json");
        var aiCache = Read<Dictionary<string, CachedClassification>>(directory, "ai-cache.json");
        var demoState = Read<DemoStateSpec>(directory, "demo-state.json");
        var spendConversion = Read<SpendConversionSpec>(directory, "spend-conversion.json");

        var bundle = new SeedBundle(
            Path.GetFullPath(directory), buyer, categories, factors, lanes, alternatives, classificationMap, aiCache, demoState, spendConversion);

        var violations = Validate(bundle);
        if (violations.Count > 0)
        {
            throw new SeedValidationException(violations);
        }
        return bundle;
    }

    /// <summary>Uses the configured directory when given; otherwise walks up from the binary and the working directory.</summary>
    public static string FindSeedDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "seed");
                if (File.Exists(Path.Combine(candidate, "factors.json")))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException("No seed/ directory found above the binary or the working directory; set SEED_DIR.");
    }

    private static List<string> Validate(SeedBundle b)
    {
        var v = new List<string>();
        var categoryIds = b.Categories.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var laneIds = b.Lanes.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var materialKeys = b.Factors.Where(f => f.Kind == FactorKind.Material).Select(f => f.Key).ToHashSet();
        var sectorKeys = b.Factors.Where(f => f.Kind == FactorKind.Spend).Select(f => f.Key).ToHashSet();
        var transportKeys = b.Factors.Where(f => f.Kind == FactorKind.Transport).Select(f => f.Key).ToHashSet();

        if (!categoryIds.Contains(Ids.Unclassified))
        {
            v.Add($"categories.json must contain {Ids.Unclassified}");
        }
        foreach (var c in b.Categories)
        {
            if (!materialKeys.Contains(c.MaterialKey)) v.Add($"category {c.Id}: no material factor for key {c.MaterialKey}");
            if (!sectorKeys.Contains(c.SectorKey)) v.Add($"category {c.Id}: no spend factor for key {c.SectorKey}");
        }
        foreach (var mode in Enum.GetValues<TransportMode>())
        {
            if (!transportKeys.Contains(mode.ToString().ToLowerInvariant())) v.Add($"factors.json: no transport factor for {mode}");
        }
        var spendUnit = $"kgCO2e/{b.SpendConversion.FactorCurrency}";
        foreach (var f in b.Factors.Where(f => f.Kind == FactorKind.Spend && f.Unit != spendUnit))
        {
            v.Add($"factor {f.Id}: unit {f.Unit}, but spend-conversion.json converts invoices to {spendUnit}");
        }
        foreach (var f in b.Factors)
        {
            if (f.Value < 0) v.Add($"factor {f.Id}: negative value");
            if (string.IsNullOrWhiteSpace(f.Source) || string.IsNullOrWhiteSpace(f.RowLabel)) v.Add($"factor {f.Id}: missing source or rowLabel");
        }
        foreach (var l in b.Lanes)
        {
            if (l.DistanceKm.GetValueOrDefault(l.ModeDefault) is null) v.Add($"lane {l.Id}: no distance for default mode {l.ModeDefault}");
        }
        foreach (var a in b.Alternatives)
        {
            if (!laneIds.Contains(a.LaneId)) v.Add($"alternative {a.Id}: unknown lane {a.LaneId}");
            if (!categoryIds.Contains(a.NodeCategory)) v.Add($"alternative {a.Id}: unknown category {a.NodeCategory}");
        }
        foreach (var m in b.ClassificationMap)
        {
            if (!categoryIds.Contains(m.CategoryId)) v.Add($"classification-map {m.ListId}/{m.Prefix}: unknown category {m.CategoryId}");
        }
        foreach (var (key, c) in b.AiCache)
        {
            if (!categoryIds.Contains(c.CategoryId)) v.Add($"ai-cache {key}: category {c.CategoryId} is not in the allow-list");
            if (c.Confidence is < 0 or > 1) v.Add($"ai-cache {key}: confidence out of range");
        }
        var ids = new List<Guid>();
        foreach (var n in b.DemoState.Nodes)
        {
            if (!laneIds.Contains(n.LaneId)) v.Add($"demo node {n.Key}: unknown lane {n.LaneId}");
            if (!File.Exists(b.FilePath(n.InvoiceFile))) v.Add($"demo node {n.Key}: invoice file missing {n.InvoiceFile}");
            if (n.Status != NodeStatus.Estimated && n.VerificationId is null) v.Add($"demo node {n.Key}: {n.Status} needs a verificationId");
            if (n.Status != NodeStatus.Estimated && string.IsNullOrWhiteSpace(n.Nonce)) v.Add($"demo node {n.Key}: {n.Status} needs a nonce");
            ids.Add(n.NodeId);
            ids.Add(n.InvoiceId);
            if (n.VerificationId is { } vid) ids.Add(vid);
        }
        if (ids.Contains(Guid.Empty)) v.Add("demo-state: empty GUID");
        if (ids.Distinct().Count() != ids.Count) v.Add("demo-state: duplicate ids");
        return v;
    }

    private static T Read<T>(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new SeedValidationException([$"{file}: empty document"]);
    }

    private sealed record LaneDto(
        string Id,
        string Origin,
        string Dest,
        TransportMode ModeDefault,
        Dictionary<string, decimal?> DistanceKm,
        Dictionary<string, int?> LeadDaysByMode,
        Dictionary<string, decimal?> CostPerKgByMode,
        string Note);

    private static Lane ToLane(LaneDto d) => new(
        d.Id, d.Origin, d.Dest, d.ModeDefault,
        ByMode(d.DistanceKm), ByMode(d.LeadDaysByMode), ByMode(d.CostPerKgByMode), d.Note);

    private static IReadOnlyDictionary<TransportMode, T?> ByMode<T>(Dictionary<string, T?> source) where T : struct
    {
        var result = new Dictionary<TransportMode, T?>();
        foreach (var mode in Enum.GetValues<TransportMode>())
        {
            result[mode] = source.TryGetValue(mode.ToString().ToLowerInvariant(), out var value) ? value : null;
        }
        return result;
    }
}
