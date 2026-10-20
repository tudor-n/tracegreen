using System.Text.RegularExpressions;
using TraceGreen.Domain.Security;

namespace TraceGreen.Tests.Domain.Security;

public class HmacLinkSignerTests
{
    private const string Secret = "unit-test-secret-0123456789abcdefghijklmnop";
    private static readonly string Hash = new('a', 64);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly HmacLinkSigner Signer = new(Secret);

    private static LinkClaims Claims(DateTimeOffset? expiresAt = null) =>
        new(Guid.Parse("11111111-1111-4111-8111-000000000001"), Hash, Signer.MintNonce(), expiresAt ?? Now.AddDays(7));

    [Fact]
    public void RoundTripsClaims()
    {
        var claims = Claims();
        var token = Signer.Sign(claims);

        var verified = Signer.Verify(token, Hash, Now);

        Assert.True(verified.IsOk);
        Assert.Equal(claims.NodeId, verified.Value.NodeId);
        Assert.Equal(claims.Nonce, verified.Value.Nonce);
        Assert.Equal(claims.InvoiceHash, verified.Value.InvoiceHash);
        Assert.Equal(claims.ExpiresAt.ToUnixTimeSeconds(), verified.Value.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void TokenIsUrlSafeAndVersioned()
    {
        var token = Signer.Sign(Claims());

        Assert.Matches(new Regex(@"^v1\.[0-9a-f-]{36}\.[0-9]+\.[A-Za-z0-9_-]{22}\.[0-9a-f]{64}\.[A-Za-z0-9_-]{43}$"), token);
    }

    [Fact]
    public void TamperedSignatureIsRejected()
    {
        var token = Signer.Sign(Claims());
        var index = token.Length - 20;
        var tampered = token[..index] + (token[index] == 'A' ? 'B' : 'A') + token[(index + 1)..];

        Assert.Equal(TokenError.BadSignature, Signer.Verify(tampered, Hash, Now).Error);
    }

    [Fact]
    public void TamperedNodeIdIsRejected()
    {
        var token = Signer.Sign(Claims());
        var tampered = token.Replace("11111111-1111-4111-8111-000000000001", "11111111-1111-4111-8111-000000000002");

        Assert.Equal(TokenError.BadSignature, Signer.Verify(tampered, Hash, Now).Error);
    }

    [Fact]
    public void DifferentSecretIsBadSignature()
    {
        var token = Signer.Sign(Claims());

        Assert.Equal(TokenError.BadSignature, new HmacLinkSigner(Secret + "x").Verify(token, Hash, Now).Error);
    }

    [Fact]
    public void ExpiredTokenIsRejected()
    {
        var token = Signer.Sign(Claims(Now.AddMinutes(5)));

        Assert.True(Signer.Verify(token, Hash, Now.AddMinutes(4)).IsOk);
        Assert.Equal(TokenError.Expired, Signer.Verify(token, Hash, Now.AddMinutes(5)).Error);
    }

    [Fact]
    public void ChangedInvoiceContentIsHashMismatch()
    {
        var token = Signer.Sign(Claims());

        Assert.Equal(TokenError.HashMismatch, Signer.Verify(token, new string('b', 64), Now).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.x")]
    [InlineData("v1.not-a-guid.1.nonce.hash.sig")]
    [InlineData("v1.11111111-1111-4111-8111-000000000001.notanumber.nonce.hash.sig")]
    [InlineData("v1.11111111-1111-4111-8111-000000000001.1.nonce.hash.!!!")]
    [InlineData("v1.11111111-1111-4111-8111-000000000001.1.nonce.hash.sig.extra")]
    public void MalformedTokensAreRejected(string token) =>
        Assert.Equal(TokenError.Malformed, Signer.Verify(token, Hash, Now).Error);

    [Fact]
    public void UnknownVersionIsMalformed()
    {
        var token = Signer.Sign(Claims());

        Assert.Equal(TokenError.Malformed, Signer.Verify("v2" + token[2..], Hash, Now).Error);
    }

    [Fact]
    public void NoncesAreRandomUrlSafeAnd22Chars()
    {
        var a = Signer.MintNonce();
        var b = Signer.MintNonce();

        Assert.NotEqual(a, b);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{22}$"), a);
    }

    [Fact]
    public void IpHashIsStableAndSecretDependent()
    {
        Assert.Equal(Hashing.IpHash("10.0.0.1", Secret), Hashing.IpHash("10.0.0.1", Secret));
        Assert.NotEqual(Hashing.IpHash("10.0.0.1", Secret), Hashing.IpHash("10.0.0.2", Secret));
        Assert.NotEqual(Hashing.IpHash("10.0.0.1", Secret), Hashing.IpHash("10.0.0.1", Secret + "x"));
        Assert.Equal(64, Hashing.IpHash("10.0.0.1", Secret).Length);
    }
}
