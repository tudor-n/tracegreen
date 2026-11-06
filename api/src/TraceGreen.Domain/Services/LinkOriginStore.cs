using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace TraceGreen.Domain.Services;

/// <summary>
/// Who made each supplier link, as the keyed hash of their address (<c>Hashing.IpHash</c>), so an answer from the same
/// network can be told apart (F7: the buyer's own staff answering for the supplier). Kept in memory for the link's lifetime,
/// like <see cref="PdfDraftStore"/>, so it works the same in both data modes and stores no address; after a restart the
/// signal is unknown. A <c>v1.</c> link is keyed by its nonce, a <c>p1.</c> supplier page link by a hash of the token.
/// </summary>
public sealed class LinkOriginStore(TimeProvider clock, ServiceOptions options)
{
    /// <summary>Plenty for a demo and a pilot; the oldest entries go first beyond it.</summary>
    public const int MaxEntries = 10_000;

    private readonly ConcurrentDictionary<string, (string IpHash, DateTimeOffset At)> _origins = new(StringComparer.Ordinal);

    public static string VerifyKey(string nonce) => "v1:" + nonce;

    public static string PortalKey(string token) => "p1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public void Remember(string key, string ipHash)
    {
        var now = clock.GetUtcNow();
        foreach (var (k, v) in _origins)
        {
            if (now - v.At > options.LinkTtl)
            {
                _origins.TryRemove(k, out _);
            }
        }
        if (_origins.Count >= MaxEntries)
        {
            foreach (var oldest in _origins.OrderBy(kv => kv.Value.At).Take(_origins.Count - MaxEntries + 1))
            {
                _origins.TryRemove(oldest.Key, out _);
            }
        }
        _origins[key] = (ipHash, now);
    }

    /// <summary>The maker's address hash, or null when the link was made before a restart (or by no request).</summary>
    public string? MakerOf(string key) => _origins.TryGetValue(key, out var origin) ? origin.IpHash : null;
}
