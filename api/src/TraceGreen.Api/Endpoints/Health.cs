using System.Diagnostics;
using System.Reflection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Api.Endpoints;

public sealed record HealthResponse(
    bool Ok,
    string Mode,
    string Db,
    int SeedNodes,
    string Version,
    double UptimeSeconds,
    bool DemoTools);

public static class HealthEndpoints
{
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();

    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (AppConfig config, IRepository repo, SeedBundle seed, CancellationToken ct) =>
            {
                var db = await repo.HealthAsync(ct);
                var seedNodes = db.Ok
                    ? (await repo.ListNodesAsync(new Ctx(seed.DemoState.WorkspaceId, "health"), ct)).Count
                    : 0;
                var response = new HealthResponse(
                    Ok: db.Ok,
                    Mode: config.DataMode.ToString().ToLowerInvariant(),
                    Db: db.Ok ? "ok" : "error: " + db.Detail,
                    SeedNodes: seedNodes,
                    Version: Version,
                    UptimeSeconds: Math.Round(Uptime.Elapsed.TotalSeconds, 1),
                    DemoTools: config.DemoTools);
                return db.Ok ? Results.Ok(response) : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("GetHealth")
            .WithTags("Health")
            .Produces<HealthResponse>()
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);
        return app;
    }
}
