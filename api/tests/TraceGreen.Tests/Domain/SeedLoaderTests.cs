using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Domain;

public class SeedLoaderTests
{
    private static SeedBundle Load() => SeedLoader.Load(TestPaths.SeedDir);

    [Fact]
    public void LoadsAndValidatesBundle()
    {
        var bundle = Load();

        Assert.Equal(TransportMode.Air, bundle.Lanes.Single(l => l.Id == "CN-RO").ModeDefault);
        Assert.True(bundle.Factors.Count > 10);
        Assert.Equal("Transilvania Electro-Assembly SRL", bundle.Buyer.Name);
        Assert.Equal(Ids.DemoWorkspace, bundle.DemoState.WorkspaceId);
        Assert.Equal(5, bundle.DemoState.Nodes.Count);
    }

    [Fact]
    public void EveryCategoryHasMaterialAndSpendFactors()
    {
        var bundle = Load();
        foreach (var category in bundle.Categories)
        {
            Assert.Contains(bundle.Factors, f => f.Kind == FactorKind.Material && f.Key == category.MaterialKey);
            Assert.Contains(bundle.Factors, f => f.Kind == FactorKind.Spend && f.Key == category.SectorKey);
        }
    }

    [Fact]
    public void EveryAlternativeAndDemoNodeReferencesExistingLaneCategoryAndInvoiceFile()
    {
        var bundle = Load();
        var laneIds = bundle.Lanes.Select(l => l.Id).ToHashSet();
        var categoryIds = bundle.Categories.Select(c => c.Id).ToHashSet();

        foreach (var alt in bundle.Alternatives)
        {
            Assert.Contains(alt.LaneId, laneIds);
            Assert.Contains(alt.NodeCategory, categoryIds);
        }
        foreach (var node in bundle.DemoState.Nodes)
        {
            Assert.Contains(node.LaneId, laneIds);
            Assert.True(File.Exists(Path.Combine(TestPaths.SeedDir, node.InvoiceFile)), node.InvoiceFile);
        }
    }

    [Fact]
    public void EveryAiCacheCategoryIsInTheAllowList()
    {
        var bundle = Load();
        var categoryIds = bundle.Categories.Select(c => c.Id).ToHashSet();
        Assert.NotEmpty(bundle.AiCache);
        Assert.All(bundle.AiCache.Values, c => Assert.Contains(c.CategoryId, categoryIds));
    }

    [Fact]
    public void AllSeedIdsAreUniqueGuids()
    {
        var bundle = Load();
        var ids = bundle.DemoState.Nodes.SelectMany(n => new[] { n.NodeId, n.InvoiceId }
            .Concat(n.VerificationId is { } v ? [v] : Array.Empty<Guid>())).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
    }

    [Fact]
    public void DemoStateMatchesTheScenario()
    {
        var bundle = Load();
        var byKey = bundle.DemoState.Nodes.ToDictionary(n => n.Key);

        Assert.False(byKey["N1"].Present);
        Assert.Equal(NodeStatus.Verified, byKey["N2"].Status);
        Assert.Equal(TransportMode.Sea, byKey["N2"].TransportMode);
        Assert.Equal(EnergySource.Grid, byKey["N2"].EnergySource);
        Assert.Equal(NodeStatus.Partial, byKey["N5"].Status);
        Assert.Null(byKey["N5"].EnergySource);
        Assert.NotNull(byKey["N5"].Nonce);
    }

    [Fact]
    public void RejectsInvalidBundleWithAllViolations()
    {
        var bad = Path.Combine(Path.GetTempPath(), "tracegreen-seed-bad-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(TestPaths.SeedDir, bad);
        File.WriteAllText(Path.Combine(bad, "alternatives.json"),
            """[{"id":"X","nodeCategory":"pcb","label":"X","country":"RO","laneId":"NOPE","costEur":1,"massT":1}]""");
        File.WriteAllText(Path.Combine(bad, "ai-cache.json"),
            """{"abc":{"categoryId":"bogus","confidence":0.5,"rationale":"r","source":"cache"}}""");
        try
        {
            var ex = Assert.Throws<SeedValidationException>(() => SeedLoader.Load(bad));
            Assert.Contains(ex.Violations, v => v.Contains("NOPE"));
            Assert.Contains(ex.Violations, v => v.Contains("bogus"));
        }
        finally
        {
            Directory.Delete(bad, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
