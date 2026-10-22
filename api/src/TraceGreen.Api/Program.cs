using Microsoft.EntityFrameworkCore;
using TraceGreen.Api;
using TraceGreen.Api.Endpoints;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;
using TraceGreen.Infrastructure.Ai;
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
builder.Services.AddSingleton<IEventBus>(sp => new InMemoryEventBus(sp.GetRequiredService<TimeProvider>()));

var aiOptions = new AiOptions(new Uri(config.AiBaseUrl + "/"), TimeSpan.FromMilliseconds(config.AiTimeoutMs));
builder.Services.AddSingleton(aiOptions);
builder.Services.AddHttpClient<IAiClient, HttpAiClient>(client =>
{
    client.BaseAddress = aiOptions.BaseUrl;
    client.Timeout = aiOptions.Timeout;
});

// Use-case services live in the Domain and get configuration through ServiceOptions, built here from AppConfig.
builder.Services.AddSingleton(new ServiceOptions(AiTimeout: TimeSpan.FromMilliseconds(config.AiTimeoutMs)));
builder.Services.AddSingleton<NodeViewMapper>();
builder.Services.AddSingleton<GraphService>();
builder.Services.AddSingleton<DemoService>();

builder.Services.AddOpenApi(options => options.AddSchemaTransformer(EventsEndpoints.RequireEventType));
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));

var app = builder.Build();

await StartupTasks.RunAsync(startup, app.Services, CancellationToken.None);
if (startup.Exit)
{
    return;
}

app.MapOpenApi();
app.MapHealth();
app.MapEvents();
app.MapGraph();
app.MapIngest();
app.MapDev();

app.Run();

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
