using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TraceGreen.Api;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

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

/// <summary>Same host with an AI client that never answers and ignores cancellation, to pin the health probe's own timeout.</summary>
public sealed class HangingAiWebAppFactory : TestWebAppFactory
{
    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IAiClient, HangingAiClient>();

    private sealed class HangingAiClient : IAiClient
    {
        public string Name => "hanging";

        public Task<IReadOnlyList<Classification>> ClassifyAsync(IReadOnlyList<ClassificationInput> lines, CancellationToken ct) =>
            new TaskCompletionSource<IReadOnlyList<Classification>>().Task;

        public Task<IReadOnlyList<Suggestion>> SuggestAsync(SupplierNode node, IReadOnlyList<Alternative> candidates, CancellationToken ct) =>
            new TaskCompletionSource<IReadOnlyList<Suggestion>>().Task;

        public Task<HealthStatus> HealthAsync(CancellationToken ct) => new TaskCompletionSource<HealthStatus>().Task;
    }
}

/// <summary>
/// For the verify loop: a clock the test moves (link expiry), a rate limit high enough that one class's calls never hit
/// it, and every log line kept in <see cref="Logs"/> so tests can prove a token never reaches one.
/// </summary>
public sealed class VerifyWebAppFactory : TestWebAppFactory
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public LogSink Logs { get; } = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(sp => AppConfig.FromProcessEnvironment() with { RateLimitPerMin = 1000 });
        services.AddSingleton<ILoggerProvider>(Logs);
    }
}

/// <summary>
/// The plan's limit of 10 calls a minute, and a test header <see cref="PeerHeader"/> that sets the connection's remote
/// address before the app's middleware runs, standing in for a phone or a proxy (TestServer has no real peer).
/// </summary>
public sealed class RateLimitedWebAppFactory : TestWebAppFactory
{
    public const string PeerHeader = "X-Test-Peer";

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(sp => AppConfig.FromProcessEnvironment() with { RateLimitPerMin = 10 });
        services.AddSingleton<IStartupFilter, PeerFilter>();
    }

    private sealed class PeerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext http, RequestDelegate nextMiddleware) =>
            {
                if (http.Request.Headers[PeerHeader].FirstOrDefault() is { } peer)
                {
                    http.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                }
                return nextMiddleware(http);
            });
            next(app);
        };
    }
}

/// <summary>Collects every formatted log message from every category.</summary>
public sealed class LogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink._messages.Enqueue($"{category}: {formatter(state, exception)} {exception}");
    }
}
