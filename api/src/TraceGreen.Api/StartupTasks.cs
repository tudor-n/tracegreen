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
    public static Task RunAsync(StartupOptions options, IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        if (options.Migrate)
        {
            logger.LogInformation("--migrate requested: no migrations are defined yet");
        }
        if (options.Seed)
        {
            logger.LogInformation("--seed requested (reset={Reset}): no repository is defined yet", options.Reset);
        }
        return Task.CompletedTask;
    }
}
