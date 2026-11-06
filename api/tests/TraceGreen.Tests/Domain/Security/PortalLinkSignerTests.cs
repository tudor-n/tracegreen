using System.Text.RegularExpressions;
using TraceGreen.Domain.Security;

namespace TraceGreen.Tests.Domain.Security;

public class PortalLinkSignerTests
{
    private const string Secret = "unit-test-secret-0123456789abcdefghijklmnop";
    private static readonly Guid Anchor = Guid.Parse("11111111-1111-4111-8111-000000000003");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly PortalLinkSigner Signer = new("portal:" + Secret);

    private static string Token(DateTimeOffset? expiresAt = null) => Signer.Sign(new PortalClaims(Anchor, expiresAt ?? Now.AddDays(7)));

    [Fact]
    public void RoundTripsClaims()
    {
        var verified = Signer.Verify(Token(), Now);

        Assert.True(verified.IsOk);
        Assert.Equal(Anchor, verified.Value.AnchorNodeId);
        Assert.Equal(Now.AddDays(7), verified.Value.ExpiresAt);
    }

    [Fact]
    public void TokenIsUrlSafeVersionedAndUnderTheLengthCap()
    {
        var token = Token();

        Assert.Matches(new Regex(@"^p1\.[0-9a-f-]{36}\.[0-9]+\.[A-Za-z0-9_-]{43}$"), token);
        Assert.True(token.Length <= PortalLinkSigner.MaxTokenLength);
    }

    [Fact]
    public void AnswersTheSameAfterAUse()
    {
        var token = Token();

        Assert.True(Signer.Verify(token, Now).IsOk);
        Assert.True(Signer.Verify(token, Now.AddHours(1)).IsOk);
    }

    [Fact]
    public void TamperedAnchorIsBadSignature()
    {
        var tampered = Token().Replace("11111111-1111-4111-8111-000000000003", "11111111-1111-4111-8111-000000000004");

        Assert.Equal(TokenError.BadSignature, Signer.Verify(tampered, Now).Error);
    }

    [Fact]
    public void TamperedExpiryIsBadSignature()
    {
        var token = Token();
        var parts = token.Split('.');
        parts[2] = (long.Parse(parts[2]) + 86_400).ToString();

        Assert.Equal(TokenError.BadSignature, Signer.Verify(string.Join('.', parts), Now).Error);
    }

    [Fact]
    public void ExpiredTokenIsExpired()
    {
        var token = Token(Now.AddMinutes(1));

        Assert.Equal(TokenError.Expired, Signer.Verify(token, Now.AddMinutes(1)).Error);
    }

    [Fact]
    public void AVerificationLinkTokenIsNotAPortalToken()
    {
        var verifyToken = new HmacLinkSigner(Secret).Sign(new LinkClaims(Anchor, new string('a', 64), "bm9uY2Utbm9uY2Utbm9uYw", Now.AddDays(7)));

        Assert.Equal(TokenError.Malformed, Signer.Verify(verifyToken, Now).Error);
    }

    [Fact]
    public void APortalTokenIsNotAVerificationLink()
    {
        var verify = new HmacLinkSigner(Secret);

        Assert.False(verify.TryReadNodeId(Token(), out _));
        Assert.Equal(TokenError.Malformed, verify.Verify(Token(), new string('a', 64), Now).Error);
    }

    [Fact]
    public void ADifferentKeyIsBadSignature()
    {
        var other = new PortalLinkSigner(Secret);

        Assert.Equal(TokenError.BadSignature, other.Verify(Token(), Now).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("p1")]
    [InlineData("p1.not-a-guid.1.abc")]
    [InlineData("p1.11111111-1111-4111-8111-000000000003.-5.abc")]
    [InlineData("p2.11111111-1111-4111-8111-000000000003.1798761600.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("p1.11111111-1111-4111-8111-000000000003.1798761600.!!!")]
    public void MalformedTokensAreMalformed(string token) =>
        Assert.Equal(TokenError.Malformed, Signer.Verify(token, Now).Error);

    [Fact]
    public void ATooLongTokenIsMalformed() =>
        Assert.Equal(TokenError.Malformed, Signer.Verify(Token() + new string('A', 200), Now).Error);

    [Fact]
    public void AShortSignatureIsBadSignature()
    {
        var parts = Token().Split('.');
        parts[3] = parts[3][..20];

        Assert.Equal(TokenError.BadSignature, Signer.Verify(string.Join('.', parts), Now).Error);
    }
}
