using System.Text.RegularExpressions;
using TraceGreen.Domain.Security;

namespace TraceGreen.Domain.Copy;

/// <summary>
/// The C# copy of <c>scripts/lib/banned-terms.mjs</c> (CLAUDE.md, "Copy rules"), used on text a model wrote before a
/// reader sees it: classification reasons, suggestion reasons and chat answers. A parity test keeps both lists equal.
/// Company names are stored only as SHA-256 hashes, exactly as in the script.
/// </summary>
public static partial class BannedTerms
{
    /// <summary>The plain terms' patterns, the same regular expressions as <c>PLAIN_TERMS</c>, in the same order.</summary>
    public static IReadOnlyList<Regex> PlainPatterns { get; } =
    [
        LedgerTerm(),
        FootprintTerm(),
        ShareTerm(),
    ];

    /// <summary><c>contains</c> entries: the length and SHA-256 of a lower-case name, matched anywhere inside a word.</summary>
    public static IReadOnlyList<(int Length, string Sha256)> Contains { get; } =
    [
        (9, "8146c43229fc8e29987913e2ada59a0cd737e3798faccc5310c9aa200899b277"),
        (7, "c27e13bdfeef173ea6328ab1f72810f8a4a8fd6aa7608786526681596d89c907"),
        (5, "0600c79833cc723f51a26a1c39b46fcc6bb909a6db9b8c9a3885469f8aaa3fc2"),
        (10, "3ce50b4077295def9dd7c77c0d63bb11d584e5d21ffc0495ac2dbb5c3cf23612"),
        (9, "fa4ad48a5a6f26fe43445091e0d9ebf705b66edf3a1f1998dd49374f817fc8a3"),
        (11, "91f44966f6e3520fac9f4c663847cad71c8da45c5f5ea1d0cf33b591ba1155de"),
    ];

    /// <summary><c>exact</c> entries: the SHA-256 of a whole word, matched in that case only.</summary>
    public static IReadOnlyList<string> Exact { get; } =
    [
        "02fb3680cd781f8fa918bdb75abe6a38d9165b849b54bdf6f71a174d69c617b2",
        "d6693a421ebd3bd40ed01f429f4cd9d45f31352982d7565a5a0ead7ec64dc373",
    ];

    private static readonly HashSet<string> ContainsHashes = Contains.Select(c => c.Sha256).ToHashSet(StringComparer.Ordinal);
    private static readonly int[] ContainsLengths = Contains.Select(c => c.Length).Distinct().ToArray();
    private static readonly HashSet<string> ExactHashes = Exact.ToHashSet(StringComparer.Ordinal);

    /// <summary>True when <paramref name="text"/> holds a plain term or a hashed name.</summary>
    public static bool Any(string text)
    {
        if (PlainPatterns.Any(p => p.IsMatch(text)))
        {
            return true;
        }
        foreach (Match word in Word().Matches(text))
        {
            if (IsHashedName(word.Value))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The text when it is clean, else null; a model's reason that fails the copy rules is not shown at all.</summary>
    public static string? Clean(string? text) => text is null || Any(text) ? null : text;

    private static bool IsHashedName(string word)
    {
        if (ExactHashes.Contains(Hashing.Sha256Hex(word)))
        {
            return true;
        }
        var lower = word.ToLowerInvariant();
        foreach (var length in ContainsLengths)
        {
            for (var i = 0; i + length <= lower.Length; i++)
            {
                if (ContainsHashes.Contains(Hashing.Sha256Hex(lower.Substring(i, length))))
                {
                    return true;
                }
            }
        }
        return false;
    }

    [GeneratedRegex(@"\bblock-?chains?\b", RegexOptions.IgnoreCase)]
    private static partial Regex LedgerTerm();

    // Ruling 2's tooltip disclaims the term, so that exact negation is allowed, as in the script.
    [GeneratedRegex(@"(?<!\bnot\s+externally\s+)\bverified\s+PCFs?\b", RegexOptions.IgnoreCase)]
    private static partial Regex FootprintTerm();

    [GeneratedRegex(@"\bprimary\s+data\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShareTerm();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
