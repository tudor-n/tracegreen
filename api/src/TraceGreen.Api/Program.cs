using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using TraceGreen.Api;
using TraceGreen.Api.Endpoints;
using TraceGreen.Domain.Ai;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;
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
// The address hash gets its own key, derived from the link secret, so one HMAC key never serves two purposes.
builder.Services.AddSingleton(new ServiceOptions(
    AiTimeout: TimeSpan.FromMilliseconds(config.AiTimeoutMs),
    LinkTtl: TimeSpan.FromSeconds(config.LinkTtlSeconds),
    IpHashKey: "ip-hash:" + config.LinkSecret));
builder.Services.AddSingleton<ILinkSigner>(new HmacLinkSigner(config.LinkSecret));
builder.Services.AddSingleton<NodeViewMapper>();
builder.Services.AddSingleton<GraphService>();
builder.Services.AddSingleton<LinkService>();
builder.Services.AddSingleton<VerifyService>();
// Scoped: they hold the typed AI HttpClient, which the client factory hands out per resolution.
builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<DemoService>();

builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer(EventsEndpoints.RequireEventType);
    options.AddSchemaTransformer(OpenApiSchemas.EnumWithoutNull);
});
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));

// The request-start line carries the query string, and /api/verify/context?t= puts a token there: never log it.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// Only the client address is taken from the proxy, and only from a trusted one; BaseUrlResolver reads the host headers itself.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var network in config.TrustedProxies)
    {
        options.KnownIPNetworks.Add(network);
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, ct) => new ValueTask(
        Problems.Result(ProblemCode.RateLimited, "Too many attempts from this address; wait a minute.").ExecuteAsync(context.HttpContext));
    // Read per request, so tests can override AppConfig the way they do for DEMO_TOOLS.
    options.AddPolicy(VerifyEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<AppConfig>().RateLimitPerMin,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

await StartupTasks.RunAsync(startup, app.Services, CancellationToken.None);
if (startup.Exit)
{
    return;
}

app.UseForwardedHeaders();
app.UseRateLimiter();

app.MapOpenApi();
app.MapHealth();
app.MapEvents();
app.MapGraph();
app.MapIngest();
app.MapVerify();
app.MapAudit();
app.MapDev();

app.Run();

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
