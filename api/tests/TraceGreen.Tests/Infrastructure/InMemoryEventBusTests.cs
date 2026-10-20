using Microsoft.Extensions.Time.Testing;
using TraceGreen.Domain.Model;
using TraceGreen.Infrastructure.Memory;

namespace TraceGreen.Tests.Infrastructure;

public class InMemoryEventBusTests
{
    private static readonly Guid Ws = Ids.DemoWorkspace;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    private static GraphReset Reset(long seq, DateTimeOffset ts) => new(seq, ts, Ws);

    [Fact]
    public void PublishAssignsIncreasingSequenceAndTimestamp()
    {
        var bus = new InMemoryEventBus(_clock);

        var first = bus.Publish(Ws, (seq, ts) => Reset(seq, ts));
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = bus.Publish(Ws, (seq, ts) => Reset(seq, ts));

        Assert.Equal(1, first.Seq);
        Assert.Equal(2, second.Seq);
        Assert.Equal(_clock.GetUtcNow(), second.Ts);
        Assert.Equal(2, bus.LastSeq(Ws));
        Assert.Equal(0, bus.LastSeq(Guid.NewGuid()));
    }

    [Fact]
    public async Task SubscriberReceivesEventsInOrderAndStopsOnCancellation()
    {
        var bus = new InMemoryEventBus(_clock);
        using var cts = new CancellationTokenSource();
        var received = new List<long>();
        var subscription = Task.Run(async () =>
        {
            await foreach (var e in bus.Subscribe(Ws, cts.Token))
            {
                received.Add(e.Seq);
            }
        }, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        bus.Publish(Ws, (seq, ts) => Reset(seq, ts));
        bus.Publish(Ws, (seq, ts) => Reset(seq, ts));
        bus.Publish(Guid.NewGuid(), (seq, ts) => new GraphReset(seq, ts, Guid.NewGuid()));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        cts.Cancel();
        await subscription;

        Assert.Equal([1, 2], received);
    }

    [Fact]
    public void ReplayReturnsLaterEventsOrAGapBeyondTheBuffer()
    {
        var bus = new InMemoryEventBus(_clock, bufferSize: 100);
        for (var i = 0; i < 150; i++)
        {
            bus.Publish(Ws, (seq, ts) => Reset(seq, ts));
        }

        var recent = bus.ReplaySince(Ws, 140);
        var tooOld = bus.ReplaySince(Ws, 20);
        var upToDate = bus.ReplaySince(Ws, 150);

        Assert.False(recent.Gap);
        Assert.Equal(Enumerable.Range(141, 10).Select(i => (long)i), recent.Events.Select(e => e.Seq));
        Assert.True(tooOld.Gap);
        Assert.False(upToDate.Gap);
        Assert.Empty(upToDate.Events);
    }

    [Fact]
    public void ReplayFromZeroOnAFreshBusIsNotAGap()
    {
        var bus = new InMemoryEventBus(_clock);

        var replay = bus.ReplaySince(Ws, 0);

        Assert.False(replay.Gap);
        Assert.Empty(replay.Events);
    }
}
