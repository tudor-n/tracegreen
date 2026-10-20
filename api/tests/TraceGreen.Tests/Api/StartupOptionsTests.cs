using TraceGreen.Api;

namespace TraceGreen.Tests.Api;

public class StartupOptionsTests
{
    [Fact]
    public void ParsesKnownFlagsAndKeepsTheRestForTheHost()
    {
        var (options, remaining) = StartupOptions.Parse(["--migrate", "--seed", "--reset", "--exit", "--urls", "http://x"]);

        Assert.True(options.Migrate);
        Assert.True(options.Seed);
        Assert.True(options.Reset);
        Assert.True(options.Exit);
        Assert.Equal(new[] { "--urls", "http://x" }, remaining);
    }

    [Fact]
    public void NoFlagsMeansServeOnly()
    {
        var (options, remaining) = StartupOptions.Parse([]);

        Assert.False(options.Migrate);
        Assert.False(options.Seed);
        Assert.False(options.Reset);
        Assert.False(options.Exit);
        Assert.Empty(remaining);
    }
}
