using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Security;

/// <summary>The supplier the link was made for is the one that issued the anchor node's invoice.</summary>
public sealed record PortalClaims(Guid AnchorNodeId, DateTimeOffset ExpiresAt);

/// <summary>
/// Signs the supplier page links (F12). Token layout: <c>p1.&lt;anchorNodeId&gt;.&lt;expiresUnix&gt;.&lt;sig&gt;</c> where
/// <c>sig = base64url(HMAC-SHA256(key, "p1|anchorNodeId|expires"))</c>. Unlike a <c>v1.</c> verification link it is
/// reusable until it expires: it opens a list, and every answer sent from that list goes through a fresh single-use
/// verification link. The host gives it its own key, derived from the link secret, so neither signer accepts the other's tokens.
/// </summary>
public sealed class PortalLinkSigner
{
    /// <summary>Real tokens are 94 characters; anything far longer is refused before it is parsed.</summary>
    public const int MaxTokenLength = 128;

    private const string Version = "p1";
    private const int SignatureBytes = 32;
    private readonly byte[] _key;

    public PortalLinkSigner(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("A signing secret is required.", nameof(secret));
        }
        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string Sign(PortalClaims claims)
    {
        var expires = claims.ExpiresAt.ToUnixTimeSeconds();
        var signature = Base64Url.EncodeToString(Signature(claims.AnchorNodeId, expires));
        return string.Join('.', Version, claims.AnchorNodeId.ToString("D"), expires.ToString(CultureInfo.InvariantCulture), signature);
    }

    /// <summary>Checks in the same order as <see cref="HmacLinkSigner"/>: format, signature (constant time), expiry.</summary>
    public Result<PortalClaims, TokenError> Verify(string token, DateTimeOffset now)
    {
        var parts = token.Split('.');
        if (token.Length > MaxTokenLength
            || parts.Length != 4
            || parts[0] != Version
            || !Guid.TryParseExact(parts[1], "D", out var nodeId)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
        {
            return Fail(TokenError.Malformed);
        }

        byte[] provided;
        try
        {
            provided = Base64Url.DecodeFromChars(parts[3]);
        }
        catch (FormatException)
        {
            return Fail(TokenError.Malformed);
        }

        if (provided.Length != SignatureBytes || !CryptographicOperations.FixedTimeEquals(provided, Signature(nodeId, expires)))
        {
            return Fail(TokenError.BadSignature);
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expires);
        return now >= expiresAt
            ? Fail(TokenError.Expired)
            : Result<PortalClaims, TokenError>.Ok(new PortalClaims(nodeId, expiresAt));
    }

    private byte[] Signature(Guid nodeId, long expires)
    {
        var message = string.Join('|', Version, nodeId.ToString("D"), expires.ToString(CultureInfo.InvariantCulture));
        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message));
    }

    private static Result<PortalClaims, TokenError> Fail(TokenError error) => Result<PortalClaims, TokenError>.Fail(error);
}
