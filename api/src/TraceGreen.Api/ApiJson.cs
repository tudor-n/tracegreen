using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceGreen.Api;

/// <summary>
/// One JSON shape for every response and every SSE message, and the source of the OpenAPI schemas the SPA's types are
/// generated from: camelCase, enums as camelCase strings, numbers as numbers, constructor parameters required and
/// nullability as annotated (so the generated TypeScript has no spurious optional or string-typed fields).
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        return options;
    }
}
