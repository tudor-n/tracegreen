using System.Text.Json.Nodes;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Ubl;

namespace TraceGreen.Tests.Domain.Security;

public class CanonicalJsonAndHashingTests
{
    private static string N1 => File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml"));

    [Fact]
    public void CanonicalJsonSortsKeysDeeplyAndOmitsWhitespace() =>
        Assert.Equal("{\"a\":{\"c\":3,\"d\":2},\"b\":1}", CanonicalJson.Serialize(new { b = 1, a = new { d = 2, c = 3 } }));

    /// <summary>
    /// How canonical JSON escapes strings, pinned against the same probe in scripts/lib/terms-hash.test.mjs, so the
    /// supplier terms' fingerprint can be recomputed without .NET (scripts/terms-hash.mjs).
    /// </summary>
    [Fact]
    public void CanonicalJsonEscapesStringsAsTheTermsHashScriptExpects()
    {
        const string Probe = """{"z":"<>&'+`\"","a":"\\ \n\t é ș „” § 😀","n":[1,true,null],"B":"x"}""";
        const string Expected =
            """{"B":"x","a":"\\ \n\t \u00E9 \u0219 \u201E\u201D \u00A7 \uD83D\uDE00","n":[1,true,null],"z":"\u003C\u003E\u0026\u0027\u002B\u0060\u0022"}""";

        Assert.Equal(Expected, CanonicalJson.Serialize(JsonNode.Parse(Probe)));
    }

    [Fact]
    public void CanonicalJsonIsStableAcrossInsertionOrder()
    {
        var first = new JsonObject { ["z"] = 1, ["a"] = new JsonArray(new JsonObject { ["y"] = true, ["x"] = null }) };
        var second = new JsonObject { ["a"] = new JsonArray(new JsonObject { ["x"] = null, ["y"] = true }), ["z"] = 1 };

        Assert.Equal(CanonicalJson.Serialize(first), CanonicalJson.Serialize(second));
        Assert.Equal("{\"a\":[{\"x\":null,\"y\":true}],\"z\":1}", CanonicalJson.Serialize(first));
    }

    [Fact]
    public void Sha256HexIsLowercaseHexOfUtf8() =>
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Hashing.Sha256Hex("abc"));

    [Fact]
    public void RawXmlHashIgnoresLineEndingsAndByteOrderMark()
    {
        var lf = N1.Replace("\r\n", "\n");
        var crlf = lf.Replace("\n", "\r\n");

        Assert.Equal(Hashing.RawXmlHash(lf), Hashing.RawXmlHash(crlf));
        Assert.Equal(Hashing.RawXmlHash(lf), Hashing.RawXmlHash("﻿" + lf));
        Assert.Equal(64, Hashing.RawXmlHash(lf).Length);
    }

    [Fact]
    public void InvoiceHashChangesWhenALineAmountChangesAndIsStableOtherwise()
    {
        var invoice = new UblInvoiceParser().Parse(N1).Value;
        var line = invoice.Lines[0];
        var tampered = invoice with { Lines = [line with { Amount = line.Amount + 1 }] };

        Assert.Equal(Hashing.InvoiceHash(invoice), Hashing.InvoiceHash(new UblInvoiceParser().Parse(N1).Value));
        Assert.NotEqual(Hashing.InvoiceHash(invoice), Hashing.InvoiceHash(tampered));
    }

    [Fact]
    public void InvoiceHashUsesEnumNamesNotNumbers()
    {
        var invoice = new UblInvoiceParser().Parse(N1).Value;
        var json = CanonicalJson.Serialize(invoice);

        Assert.Contains("\"issueDate\":\"2026-10-28\"", json);
        Assert.Contains("\"unitCode\":\"KGM\"", json);
    }
}
