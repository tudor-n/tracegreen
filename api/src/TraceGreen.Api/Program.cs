using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TraceGreen.Api;
using TraceGreen.Api.Endpoints;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;
using TraceGreen.Infrastructure.Memory;
using TraceGreen.Infrastructure.Postgres;

var (startup, hostArgs) = StartupOptions.Parse(args);
var builder = WebApplication.CreateBuilder(hostArgs);

var config = AppConfig.FromProcessEnvironment();
var seed = SeedLoader.Load(SeedLoader.FindSeedDirectory(config.SeedDir));

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(seed);
builder.Services.AddSingleton(TimeProvider.System);
if (config.DataMode == DataMode.Postgres)
{
    builder.Services.AddDbContextFactory<TraceGreenDbContext>(options =>
        options.UseNpgsql(ConnectionStrings.FromUrl(config.DatabaseUrl!)));
    builder.Services.AddSingleton<IRepository, EfRepository>();
}
else
{
    builder.Services.AddSingleton<IRepository, MemoryRepository>();
}

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

var app = builder.Build();

await StartupTasks.RunAsync(startup, app.Services, CancellationToken.None);
if (startup.Exit)
{
    return;
}

app.MapOpenApi();
app.MapHealth();

app.Run();

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
