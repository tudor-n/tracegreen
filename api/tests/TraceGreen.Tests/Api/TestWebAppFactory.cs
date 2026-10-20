using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TraceGreen.Tests.Api;

/// <summary>Hosts the API in memory mode with demo tools on, regardless of the developer's environment.</summary>
public sealed class TestWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DATA_MODE", "memory");
        Environment.SetEnvironmentVariable("DEMO_TOOLS", "1");
        Environment.SetEnvironmentVariable("LINK_SECRET", "integration-test-secret-0123456789abcdef");
        Environment.SetEnvironmentVariable("SEED_DIR", TestPaths.SeedDir);
        builder.UseEnvironment("Development");
    }
}
