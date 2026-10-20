using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TraceGreen.Domain.Security;

/// <summary>
/// Deterministic JSON: object keys sorted ordinally at every level, no whitespace, camelCase names, enums as strings.
/// Used for invoice content hashes and audit-chain hashes, so it must never change silently.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static string Serialize(object? value) =>
        Serialize(value as JsonNode ?? JsonSerializer.SerializeToNode(value, Options));

    public static string Serialize(JsonNode? node) =>
        Canonicalize(node)?.ToJsonString(Compact) ?? "null";

    public static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => KeyValuePair.Create(kv.Key, Canonicalize(kv.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonicalize).ToArray()),
        JsonValue value => value.DeepClone(),
        null => null,
        _ => node.DeepClone(),
    };
}
