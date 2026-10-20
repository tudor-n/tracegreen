using System.Text.Json;
using System.Text.Json.Serialization;
using TraceGreen.Api;
using TraceGreen.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var config = AppConfig.FromProcessEnvironment();
builder.Services.AddSingleton(config);
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

var app = builder.Build();

app.MapOpenApi();
app.MapHealth();

app.Run();

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
