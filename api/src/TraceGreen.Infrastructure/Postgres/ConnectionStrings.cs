using System.Globalization;

namespace TraceGreen.Infrastructure.Postgres;

public static class ConnectionStrings
{
    /// <summary>Converts a <c>postgres://user:password@host:port/database?sslmode=…</c> URL into an Npgsql connection string.</summary>
    public static string FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("postgres" or "postgresql"))
        {
            throw new ArgumentException("DATABASE_URL must be a postgres:// or postgresql:// URL.", nameof(url));
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var port = uri.IsDefaultPort ? 5432 : uri.Port;

        var parts = new List<string>
        {
            $"Host={uri.Host}",
            $"Port={port.ToString(CultureInfo.InvariantCulture)}",
            $"Database={database}",
            $"Username={username}",
            $"Password={password}",
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                var mode = Uri.UnescapeDataString(kv[1]);
                parts.Add("SSL Mode=" + char.ToUpperInvariant(mode[0]) + mode[1..].ToLowerInvariant());
            }
        }
        return string.Join(';', parts);
    }
}
