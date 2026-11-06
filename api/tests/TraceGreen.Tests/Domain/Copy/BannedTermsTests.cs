using System.Text.RegularExpressions;
using TraceGreen.Domain.Copy;

namespace TraceGreen.Tests.Domain.Copy;

public sealed partial class BannedTermsTests
{
    private static readonly string Script = File.ReadAllText(
        Path.Combine(Directory.GetParent(TestPaths.SeedDir)!.FullName, "scripts", "lib", "banned-terms.mjs"));

    [Fact]
    public void ContainsHashesMatchTheScript()
    {
        var fromScript = ContainsEntry().Matches(Script)
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value))
            .ToArray();
        Assert.NotEmpty(fromScript);
        Assert.Equal(fromScript.Order(), BannedTerms.Contains.Order());
    }

    [Fact]
    public void ExactHashesMatchTheScript()
    {
        var exactLists = ExactList().Matches(Script).Select(m => m.Groups[1].Value);
        var fromScript = exactLists.SelectMany(list => Hex().Matches(list).Select(h => h.Value)).ToArray();
        Assert.NotEmpty(fromScript);
        Assert.Equal(fromScript.Order(), BannedTerms.Exact.Order());
    }

    [Fact]
    public void PlainPatternsMatchTheScript()
    {
        var fromScript = PlainPattern().Matches(Script).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(fromScript, BannedTerms.PlainPatterns.Select(p => p.ToString()));
        Assert.All(BannedTerms.PlainPatterns, p => Assert.True(p.Options.HasFlag(RegexOptions.IgnoreCase)));
    }

    [Theory]
    [InlineData("Zinc die-cast housing with alloy covers")]
    [InlineData("Printed circuit boards, two layers")]
    [InlineData("Supplier-confirmed share, not externally verified PCFs")]
    public void OrdinaryTextIsClean(string text)
    {
        Assert.False(BannedTerms.Any(text));
        Assert.Equal(text, BannedTerms.Clean(text));
    }

    [Fact]
    public void PlainTermsAreCaught()
    {
        // Built at run time so this file itself passes the copy guard.
        string[] banned = ["Stored on a " + "block" + "chain", "a verified " + "PCF for this part", "uses primary " + "data"];
        Assert.All(banned, text =>
        {
            Assert.True(BannedTerms.Any(text));
            Assert.Null(BannedTerms.Clean(text));
        });
    }

    [Fact]
    public void NullStaysNull() => Assert.Null(BannedTerms.Clean(null));

    [GeneratedRegex(@"\{\s*length:\s*(\d+),\s*sha256:\s*'([0-9a-f]{64})'\s*\}")]
    private static partial Regex ContainsEntry();

    [GeneratedRegex(@"exact:\s*\[([^\]]*)\]")]
    private static partial Regex ExactList();

    [GeneratedRegex(@"[0-9a-f]{64}")]
    private static partial Regex Hex();

    [GeneratedRegex(@"pattern:\s*/(.+?)/i,")]
    private static partial Regex PlainPattern();
}
