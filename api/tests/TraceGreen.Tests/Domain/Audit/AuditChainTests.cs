using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;

namespace TraceGreen.Tests.Domain.Audit;

public class AuditChainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private static List<AuditEntry> Chain(int count)
    {
        var entries = new List<AuditEntry>();
        AuditEntry? prev = null;
        for (var i = 1; i <= count; i++)
        {
            var built = AuditChain.Build(prev, Ids.DemoWorkspace, T0.AddSeconds(i), "buyer", "test.action", new JsonObject { ["n"] = i });
            var entry = AuditChain.WithId(built, i);
            entries.Add(entry);
            prev = entry;
        }
        return entries;
    }

    [Fact]
    public void GenesisPrevHashIs64Zeros()
    {
        Assert.Equal(new string('0', 64), AuditChain.GenesisHash);
        Assert.Equal(AuditChain.GenesisHash, Chain(1)[0].PrevHash);
    }

    [Fact]
    public void BuiltEntriesLinkToThePreviousHashAndVerify()
    {
        var entries = Chain(3);

        Assert.Equal(entries[0].Hash, entries[1].PrevHash);
        Assert.Equal(entries[1].Hash, entries[2].PrevHash);
        var verification = AuditChain.Verify(entries);
        Assert.True(verification.Valid);
        Assert.Equal(3, verification.Length);
        Assert.Null(verification.BrokenAt);
    }

    [Fact]
    public void EditingThePayloadOfTheSecondEntryBreaksAtTwo()
    {
        var entries = Chain(3);
        entries[1] = entries[1] with { Payload = new JsonObject { ["n"] = 99 } };

        var verification = AuditChain.Verify(entries);

        Assert.False(verification.Valid);
        Assert.Equal(2, verification.BrokenAt);
    }

    [Fact]
    public void EditingActorOrActionBreaksTheChain()
    {
        var entries = Chain(2);
        entries[1] = entries[1] with { Actor = "someone-else" };

        Assert.Equal(2, AuditChain.Verify(entries).BrokenAt);
    }

    [Fact]
    public void ReorderingEntriesBreaksTheChain()
    {
        var entries = Chain(3);
        (entries[1], entries[2]) = (entries[2], entries[1]);

        Assert.Equal(2, AuditChain.Verify(entries).BrokenAt);
    }

    [Fact]
    public void EmptyChainIsValidWithLengthZero()
    {
        var verification = AuditChain.Verify([]);

        Assert.True(verification.Valid);
        Assert.Equal(0, verification.Length);
    }

    [Fact]
    public void TimestampsAreNormalisedToUtcMilliseconds()
    {
        var local = new DateTimeOffset(2026, 11, 6, 12, 0, 0, 123, TimeSpan.FromHours(2)).AddTicks(4567);

        var built = AuditChain.Build(null, Ids.DemoWorkspace, local, "buyer", "x", new JsonObject());

        Assert.Equal(TimeSpan.Zero, built.Ts.Offset);
        Assert.Equal(0, built.Ts.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(local.ToUnixTimeMilliseconds(), built.Ts.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void HashIsDeterministicForTheSameInputs()
    {
        var a = AuditChain.Build(null, Ids.DemoWorkspace, T0, "buyer", "x", new JsonObject { ["b"] = 1, ["a"] = 2 });
        var b = AuditChain.Build(null, Ids.DemoWorkspace, T0, "buyer", "x", new JsonObject { ["a"] = 2, ["b"] = 1 });

        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(64, a.Hash.Length);
    }
}
