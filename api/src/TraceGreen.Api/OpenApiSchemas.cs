using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TraceGreen.Api;

public static class OpenApiSchemas
{
    /// <summary>
    /// .NET folds <c>null</c> into an enum's component when it first meets the enum as a nullable property, which would
    /// make the generated TypeScript enum nullable everywhere (e.g. <c>Record&lt;EnergySource, string&gt;</c> fails).
    /// Nullable properties already say so through <c>oneOf [null, $ref]</c>, so the component keeps only real values.
    /// </summary>
    public static Task EnumWithoutNull(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type.IsEnum && schema.Enum is { } values)
        {
            for (var i = values.Count - 1; i >= 0; i--)
            {
                if (values[i] is null || values[i]!.GetValueKind() == System.Text.Json.JsonValueKind.Null)
                {
                    values.RemoveAt(i);
                }
            }
            if (schema.Type is { } t && t.HasFlag(JsonSchemaType.Null))
            {
                schema.Type = t & ~JsonSchemaType.Null;
            }
        }
        return Task.CompletedTask;
    }
}
