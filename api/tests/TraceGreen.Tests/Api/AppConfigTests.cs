using TraceGreen.Api;

namespace TraceGreen.Tests.Api;

public class AppConfigTests
{
    private static Dictionary<string, string?> Env(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => (string?)p.Value);

    private static readonly string LongSecret = new('x', 32);

    [Fact]
    public void DefaultsToMemoryWithoutDatabaseUrl() =>
        Assert.Equal(DataMode.Memory, AppConfig.FromEnvironment(Env(("LINK_SECRET", LongSecret))).DataMode);

    [Fact]
    public void SelectsPostgresWhenDatabaseUrlSet() =>
        Assert.Equal(DataMode.Postgres, AppConfig.FromEnvironment(Env(("LINK_SECRET", LongSecret), ("DATABASE_URL", "postgres://a"))).DataMode);

    [Fact]
    public void SeedModeOverridesDatabaseUrl() =>
        Assert.Equal(DataMode.Memory, AppConfig.FromEnvironment(Env(("LINK_SECRET", LongSecret), ("DATABASE_URL", "postgres://a"), ("SEED_MODE", "1"))).DataMode);

    [Fact]
    public void ProductionRefusesShortSecret() =>
        Assert.Throws<InvalidOperationException>(() =>
            AppConfig.FromEnvironment(Env(("ASPNETCORE_ENVIRONMENT", "Production"), ("LINK_SECRET", "short"))));

    [Fact]
    public void DevelopmentFallsBackToFixedSecret() =>
        Assert.True(AppConfig.FromEnvironment(Env()).LinkSecret.Length >= 32);

    [Fact]
    public void DefaultsMatchThePlan()
    {
        var c = AppConfig.FromEnvironment(Env());
        Assert.Equal(604800, c.LinkTtlSeconds);
        Assert.Equal(70m, c.EtsPriceEurPerT);
        Assert.Equal(10000, c.AiTimeoutMs);
        Assert.Equal(10, c.RateLimitPerMin);
        Assert.False(c.DemoTools);
        Assert.Equal("http://localhost:8000", c.AiBaseUrl);
    }
}
