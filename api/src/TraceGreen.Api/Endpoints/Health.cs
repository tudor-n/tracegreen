using System.Diagnostics;
using System.Reflection;

namespace TraceGreen.Api.Endpoints;

public sealed record HealthResponse(bool Ok, string Mode, string Version, double UptimeSeconds, bool DemoTools);

public static class HealthEndpoints
{
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();

    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", (AppConfig config) =>
                TypedResults.Ok(new HealthResponse(
                    Ok: true,
                    Mode: config.DataMode.ToString().ToLowerInvariant(),
                    Version: Version,
                    UptimeSeconds: Math.Round(Uptime.Elapsed.TotalSeconds, 1),
                    DemoTools: config.DemoTools)))
            .WithName("GetHealth")
            .WithTags("Health");
        return app;
    }
}
