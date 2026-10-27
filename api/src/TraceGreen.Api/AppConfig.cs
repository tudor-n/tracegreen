using System.Globalization;
using System.Net;

namespace TraceGreen.Api;

public enum DataMode { Memory, Postgres }

/// <summary>
/// Process configuration read once from environment variables. Fails fast on invalid values.
/// </summary>
public sealed record AppConfig(
    DataMode DataMode,
    string? DatabaseUrl,
    string LinkSecret,
    int LinkTtlSeconds,
    bool DemoTools,
    string? PublicBaseUrl,
    decimal EtsPriceEurPerT,
    string AiBaseUrl,
    int AiTimeoutMs,
    int RateLimitPerMin,
    string? SeedDir,
    string EnvironmentName,
    IReadOnlyList<IPNetwork> TrustedProxies)
{
    /// <summary>
    /// Proxies whose <c>X-Forwarded-For</c> is believed: loopback (Vite on the host) and Docker's default address pool
    /// (Vite or nginx in Compose). A phone on the hotspot is in neither, so it cannot pick its own rate-limit bucket.
    /// </summary>
    public const string DefaultTrustedProxies = "127.0.0.0/8,::1/128,172.16.0.0/12";

    public const string DevelopmentSecret = "dev-only-secret-change-me-0123456789abcdef";
    public const int MinSecretLength = 32;

    public bool IsProduction => string.Equals(EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase);

    public static AppConfig FromProcessEnvironment()
    {
        var env = new Dictionary<string, string?>();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            env[(string)e.Key] = e.Value as string;
        }
        return FromEnvironment(env);
    }

    public static AppConfig FromEnvironment(IDictionary<string, string?> env)
    {
        var environmentName = Get(env, "ASPNETCORE_ENVIRONMENT") ?? Get(env, "DOTNET_ENVIRONMENT") ?? "Development";
        var isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

        var databaseUrl = Get(env, "DATABASE_URL");
        var seedMode = Get(env, "SEED_MODE") == "1";
        var explicitMode = Get(env, "DATA_MODE");
        var dataMode = explicitMode?.ToLowerInvariant() switch
        {
            "memory" => DataMode.Memory,
            "postgres" => DataMode.Postgres,
            null or "" => !seedMode && databaseUrl is not null ? DataMode.Postgres : DataMode.Memory,
            _ => throw new InvalidOperationException($"DATA_MODE must be memory or postgres, got {explicitMode}."),
        };
        if (dataMode == DataMode.Postgres && databaseUrl is null)
        {
            throw new InvalidOperationException("DATA_MODE=postgres requires DATABASE_URL.");
        }

        var secret = Get(env, "LINK_SECRET");
        if (secret is null)
        {
            if (isProduction)
            {
                throw new InvalidOperationException("LINK_SECRET is required in Production.");
            }
            secret = DevelopmentSecret;
        }
        if (secret.Length < MinSecretLength)
        {
            throw new InvalidOperationException($"LINK_SECRET must be at least {MinSecretLength} characters.");
        }

        return new AppConfig(
            DataMode: dataMode,
            DatabaseUrl: databaseUrl,
            LinkSecret: secret,
            LinkTtlSeconds: GetInt(env, "LINK_TTL_SECONDS", 604800),
            DemoTools: Get(env, "DEMO_TOOLS") == "1",
            PublicBaseUrl: Get(env, "PUBLIC_BASE_URL")?.TrimEnd('/'),
            EtsPriceEurPerT: GetDecimal(env, "ETS_PRICE_EUR_PER_T", 70m),
            AiBaseUrl: (Get(env, "AI_BASE_URL") ?? "http://localhost:8000").TrimEnd('/'),
            AiTimeoutMs: GetInt(env, "AI_TIMEOUT_MS", 10000),
            RateLimitPerMin: GetInt(env, "RATE_LIMIT_PER_MIN", 10),
            SeedDir: Get(env, "SEED_DIR"),
            EnvironmentName: environmentName,
            TrustedProxies: GetNetworks(env, "TRUSTED_PROXIES", DefaultTrustedProxies));
    }

    private static string? Get(IDictionary<string, string?> env, string key) =>
        env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static int GetInt(IDictionary<string, string?> env, string key, int fallback)
    {
        var raw = Get(env, key);
        if (raw is null)
        {
            return fallback;
        }
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : throw new InvalidOperationException($"{key} must be a positive integer, got {raw}.");
    }

    private static IReadOnlyList<IPNetwork> GetNetworks(IDictionary<string, string?> env, string key, string fallback)
    {
        var raw = Get(env, key) ?? fallback;
        var networks = new List<IPNetwork>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            networks.Add(IPNetwork.TryParse(part, out var network)
                ? network
                : throw new InvalidOperationException($"{key} must be a comma-separated list of CIDR ranges, got {part}."));
        }
        return networks;
    }

    private static decimal GetDecimal(IDictionary<string, string?> env, string key, decimal fallback)
    {
        var raw = Get(env, key);
        if (raw is null)
        {
            return fallback;
        }
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : throw new InvalidOperationException($"{key} must be a non-negative number, got {raw}.");
    }
}
