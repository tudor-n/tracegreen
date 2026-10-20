using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Security;

public enum TokenError { Malformed, BadSignature, Expired, HashMismatch }

public sealed record LinkClaims(Guid NodeId, string InvoiceHash, string Nonce, DateTimeOffset ExpiresAt);

/// <summary>
/// Signs and verifies supplier verification links (C1–C2). The signer is pure: single use is the repository's job.
/// </summary>
public interface ILinkSigner
{
    string MintNonce();

    string Sign(LinkClaims claims);

    Result<LinkClaims, TokenError> Verify(string token, string expectedInvoiceHash, DateTimeOffset now);
}
