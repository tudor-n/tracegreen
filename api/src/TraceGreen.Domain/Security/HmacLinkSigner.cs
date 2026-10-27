using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Security;

/// <summary>
/// Token layout: <c>v1.&lt;nodeId&gt;.&lt;expiresUnix&gt;.&lt;nonce&gt;.&lt;invoiceHash&gt;.&lt;sig&gt;</c> where
/// <c>sig = base64url(HMAC-SHA256(secret, nodeId|expires|nonce|invoiceHash))</c>.
/// Carrying the invoice hash lets verification tell a tampered token (401) from a changed invoice (410).
/// </summary>
public sealed class HmacLinkSigner : ILinkSigner
{
    private const string Version = "v1";
    private const int SignatureBytes = 32;
    private readonly byte[] _key;

    public HmacLinkSigner(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("A signing secret is required.", nameof(secret));
        }
        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string MintNonce() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    public string Sign(LinkClaims claims)
    {
        var expires = claims.ExpiresAt.ToUnixTimeSeconds();
        var signature = Base64Url.EncodeToString(Signature(claims.NodeId, expires, claims.Nonce, claims.InvoiceHash));
        return string.Join('.', Version, claims.NodeId.ToString("D"), expires.ToString(CultureInfo.InvariantCulture), claims.Nonce, claims.InvoiceHash, signature);
    }

    public Result<LinkClaims, TokenError> Verify(string token, string expectedInvoiceHash, DateTimeOffset now)
    {
        var parts = token.Split('.');
        if (parts.Length != 6 || parts[0] != Version)
        {
            return Fail(TokenError.Malformed);
        }
        if (!Guid.TryParseExact(parts[1], "D", out var nodeId)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || !IsBase64Url(parts[3])
            || !IsHex64(parts[4]))
        {
            return Fail(TokenError.Malformed);
        }

        byte[] provided;
        try
        {
            provided = Base64Url.DecodeFromChars(parts[5]);
        }
        catch (FormatException)
        {
            return Fail(TokenError.Malformed);
        }

        var nonce = parts[3];
        var invoiceHash = parts[4];
        var expected = Signature(nodeId, expires, nonce, invoiceHash);
        if (provided.Length != SignatureBytes || !CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            return Fail(TokenError.BadSignature);
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expires);
        if (now >= expiresAt)
        {
            return Fail(TokenError.Expired);
        }
        if (!string.Equals(invoiceHash, expectedInvoiceHash, StringComparison.Ordinal))
        {
            return Fail(TokenError.HashMismatch);
        }
        return Result<LinkClaims, TokenError>.Ok(new LinkClaims(nodeId, invoiceHash, nonce, expiresAt));
    }

    public bool TryReadNodeId(string token, out Guid nodeId)
    {
        nodeId = Guid.Empty;
        var parts = token.Split('.');
        return parts.Length == 6 && parts[0] == Version && Guid.TryParseExact(parts[1], "D", out nodeId);
    }

    private byte[] Signature(Guid nodeId, long expires, string nonce, string invoiceHash)
    {
        var message = string.Join('|', nodeId.ToString("D"), expires.ToString(CultureInfo.InvariantCulture), nonce, invoiceHash);
        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message));
    }

    private static bool IsBase64Url(string s) =>
        s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsHex64(string s) =>
        s.Length == 64 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Result<LinkClaims, TokenError> Fail(TokenError error) => Result<LinkClaims, TokenError>.Fail(error);
}
