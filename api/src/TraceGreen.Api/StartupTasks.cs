using Microsoft.EntityFrameworkCore;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;
using TraceGreen.Infrastructure.Postgres;

namespace TraceGreen.Api;

/// <summary>
/// Process flags handled before the host starts: <c>--migrate</c> applies database migrations,
/// <c>--seed</c> loads the seed bundle (idempotent; <c>--reset</c> wipes the demo workspace first),
/// <c>--exit</c> stops after those tasks instead of serving (used by the one-shot migrate container).
/// </summary>
public sealed record StartupOptions(bool Migrate, bool Seed, bool Reset, bool Exit)
{
    public static (StartupOptions Options, string[] Remaining) Parse(IEnumerable<string> args)
    {
        var migrate = false;
        var seed = false;
        var reset = false;
        var exit = false;
        var remaining = new List<string>();
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--migrate": migrate = true; break;
                case "--seed": seed = true; break;
                case "--reset": reset = true; break;
                case "--exit": exit = true; break;
                default: remaining.Add(arg); break;
            }
        }
        return (new StartupOptions(migrate, seed, reset, exit), remaining.ToArray());
    }
}

public static class StartupTasks
{
    public static async Task RunAsync(StartupOptions options, IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var config = services.GetRequiredService<AppConfig>();
        var bundle = services.GetRequiredService<SeedBundle>();
        var repo = services.GetRequiredService<IRepository>();

        logger.LogInformation("Data mode {Mode}; seed directory {SeedDir}", config.DataMode, bundle.Directory);

        if (config.DataMode == DataMode.Postgres && options.Migrate)
        {
            var factory = services.GetRequiredService<IDbContextFactory<TraceGreenDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Migrations applied: {Count} pending before start", pending.Count);
        }

        // The in-memory store starts empty, so memory mode always seeds; Postgres seeds only when asked.
        if (options.Seed || config.DataMode == DataMode.Memory)
        {
            await repo.SeedAsync(bundle, reset: options.Reset, ct);
            var nodes = await repo.ListNodesAsync(new Ctx(bundle.DemoState.WorkspaceId, "startup"), ct);
            logger.LogInformation("Seed loaded (reset={Reset}): {Nodes} nodes in the demo workspace", options.Reset, nodes.Count);
        }
    }
}
