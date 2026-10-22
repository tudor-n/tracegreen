using System.Diagnostics;
using System.Reflection;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// <see cref="Ai"/> is the AI provider name, or <c>error: …</c> when the service does not answer within
/// <see cref="HealthEndpoints.AiProbeTimeout"/>; the AI is optional, so it never makes <see cref="Ok"/> false.
/// <see cref="BaseUrl"/> is the origin verification links are built on.
/// </summary>
public sealed record HealthResponse(
    bool Ok,
    string Mode,
    string Db,
    int SeedNodes,
    string Version,
    double UptimeSeconds,
    bool DemoTools,
    string Ai,
    BaseUrlInfo BaseUrl);

public static class HealthEndpoints
{
    public static readonly TimeSpan AiProbeTimeout = TimeSpan.FromMilliseconds(1500);

    private static readonly Stopwatch Uptime = Stopwatch.StartNew();

    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (HttpRequest request, AppConfig config, IRepository repo, IAiClient ai, SeedBundle seed, CancellationToken ct) =>
            {
                var aiStatus = AiStatusAsync(ai, ct);
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
                    DemoTools: config.DemoTools,
                    Ai: await aiStatus,
                    BaseUrl: BaseUrlResolver.Resolve(request, config.PublicBaseUrl));
                return db.Ok ? Results.Ok(response) : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("GetHealth")
            .WithTags("Health")
            .Produces<HealthResponse>()
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<string> AiStatusAsync(IAiClient ai, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(AiProbeTimeout);
        try
        {
            var status = await ai.HealthAsync(cts.Token).WaitAsync(AiProbeTimeout, ct);
            return status.Ok ? status.Detail ?? ai.Name : "error: " + status.Detail;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return ex is TimeoutException or OperationCanceledException
                ? $"error: no answer within {AiProbeTimeout.TotalSeconds:0.#} s"
                : "error: " + ex.GetType().Name;
        }
    }
}
