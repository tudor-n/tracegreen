using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Api;

namespace TraceGreen.Tests.Api;

/// <summary>
/// Hosts the API in memory mode with demo tools on, regardless of the developer's environment. The AI service points at
/// a closed port so classification falls back immediately instead of reaching whatever listens on :8000.
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DATA_MODE", "memory");
        Environment.SetEnvironmentVariable("DEMO_TOOLS", "1");
        Environment.SetEnvironmentVariable("LINK_SECRET", "integration-test-secret-0123456789abcdef");
        Environment.SetEnvironmentVariable("SEED_DIR", TestPaths.SeedDir);
        Environment.SetEnvironmentVariable("AI_BASE_URL", "http://127.0.0.1:9");
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(ConfigureServices);
    }

    /// <summary>Runs after the app's own registrations, so a later registration replaces what <c>Program.cs</c> added.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }
}

/// <summary>Same host with <c>DEMO_TOOLS</c> off. Overrides the registered config instead of the process environment, which parallel tests share.</summary>
public sealed class NoDemoToolsWebAppFactory : TestWebAppFactory
{
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton(sp => AppConfig.FromProcessEnvironment() with { DemoTools = false });
}
