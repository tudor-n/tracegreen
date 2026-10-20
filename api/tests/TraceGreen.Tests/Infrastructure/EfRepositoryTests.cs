using Microsoft.EntityFrameworkCore;
using TraceGreen.Domain.Ports;
using TraceGreen.Infrastructure.Postgres;
using TraceGreen.Tests.Contracts;

namespace TraceGreen.Tests.Infrastructure;

/// <summary>Runs the repository contract against a real Postgres when DATABASE_URL is set (Compose: tracegreen_test).</summary>
public class EfRepositoryTests : RepositoryContract
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("DATABASE_URL");
    private static readonly SemaphoreSlim MigrateOnce = new(1, 1);
    private static bool _migrated;

    protected override async Task<IRepository> MakeAsync()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Url), "DATABASE_URL is not set; Postgres contract skipped.");

        var options = new DbContextOptionsBuilder<TraceGreenDbContext>()
            .UseNpgsql(ConnectionStrings.FromUrl(Url!))
            .Options;
        var factory = new StaticFactory(options);

        await MigrateOnce.WaitAsync();
        try
        {
            if (!_migrated)
            {
                await using var db = factory.CreateDbContext();
                await db.Database.MigrateAsync();
                _migrated = true;
            }
        }
        finally
        {
            MigrateOnce.Release();
        }

        var repo = new EfRepository(factory);
        await repo.SeedAsync(Bundle, reset: true, CancellationToken.None);
        return repo;
    }

    private sealed class StaticFactory(DbContextOptions<TraceGreenDbContext> options) : IDbContextFactory<TraceGreenDbContext>
    {
        public TraceGreenDbContext CreateDbContext() => new(options);
    }
}
