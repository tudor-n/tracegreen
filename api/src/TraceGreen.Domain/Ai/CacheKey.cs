using System.Text.RegularExpressions;
using TraceGreen.Domain.Security;

namespace TraceGreen.Domain.Ai;

/// <summary>
/// Key for the classification cache: SHA-256 of the description after trim, lower-case and whitespace collapse.
/// The Python service computes the same key (ai/app/cache.py); keep both in step.
/// </summary>
public static partial class CacheKey
{
    public static string Compute(string description) =>
        Hashing.Sha256Hex(Whitespace().Replace(description.Trim().ToLowerInvariant(), " "));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
