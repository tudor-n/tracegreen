using System.Net;

namespace TraceGreen.Api;

/// <summary>Where the browser-facing base URL came from. Serialised "configured" | "forwarded" | "request".</summary>
public enum BaseUrlSource { Configured, Forwarded, Request }

/// <summary><see cref="Loopback"/> is true when a phone could not open links built on <see cref="Url"/>.</summary>
public sealed record BaseUrlInfo(string Url, bool Loopback, BaseUrlSource Source);

/// <summary>
/// The origin verification links are built on: <c>PUBLIC_BASE_URL</c> when set, else the proxy's
/// <c>X-Forwarded-Proto</c>/<c>X-Forwarded-Host</c> (Vite and nginx set them to the browser-facing host), else the
/// request's own origin. A loopback result is flagged so the QR card can warn before anyone scans it.
/// For a header with several values the last one wins: an appending proxy (Vite's <c>xfwd</c>) adds its own value
/// after whatever the client sent. A direct caller can still choose these headers, so the result is a display hint,
/// never something to trust (for example in audit payloads).
/// </summary>
public static class BaseUrlResolver
{
    public static BaseUrlInfo Resolve(HttpRequest request, string? publicBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            var configured = publicBaseUrl.TrimEnd('/');
            return new BaseUrlInfo(configured, IsLoopback(configured), BaseUrlSource.Configured);
        }

        var forwardedHost = Last(request.Headers["X-Forwarded-Host"]);
        if (forwardedHost is not null)
        {
            var proto = Last(request.Headers["X-Forwarded-Proto"]) is { } p && p is "http" or "https" ? p : request.Scheme;
            if (Origin($"{proto}://{forwardedHost}") is { } forwarded)
            {
                return new BaseUrlInfo(forwarded, IsLoopback(forwarded), BaseUrlSource.Forwarded);
            }
        }

        var own = $"{request.Scheme}://{request.Host.Value}";
        return new BaseUrlInfo(own, IsLoopback(own), BaseUrlSource.Request);
    }

    public static bool IsLoopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return true;
        }
        var host = uri.IdnHost.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return IPAddress.TryParse(host, out var ip)
            && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));
    }

    private static string? Last(Microsoft.Extensions.Primitives.StringValues values)
    {
        var last = values.LastOrDefault()?.Split(',')[^1].Trim();
        return string.IsNullOrEmpty(last) ? null : last;
    }

    /// <summary>Accepts a bare scheme://host[:port] only, so a forged header cannot smuggle a path or credentials into links.</summary>
    private static string? Origin(string candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.PathAndQuery == "/"
        && string.IsNullOrEmpty(uri.Fragment)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
}
