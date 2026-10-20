using System.Security.Cryptography;
using System.Text;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Security;

public static class Hashing
{
    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Strips a byte-order mark and normalises line endings so Windows and Unix checkouts hash alike.</summary>
    public static string NormalizeXml(string xml) =>
        xml.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');

    public static string RawXmlHash(string xml) => Sha256Hex(NormalizeXml(xml));

    /// <summary>Content hash the verification token is bound to (C2): canonical JSON of the extracted fields.</summary>
    public static string InvoiceHash(ExtractedInvoice invoice) => Sha256Hex(CanonicalJson.Serialize(invoice));

    /// <summary>Keyed hash of a client address for rate limiting and audit without storing the address itself (C7).</summary>
    public static string IpHash(string ip, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(ip)));
}
