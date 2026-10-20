using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Infrastructure.Memory;

/// <summary>
/// Single-process event bus: a per-workspace sequence, a ring buffer for replays, and one unbounded channel per
/// subscriber. Good for one API instance; swap for LISTEN/NOTIFY or a hosted realtime service to scale out.
/// </summary>
public sealed class InMemoryEventBus(TimeProvider clock, int bufferSize = 500) : IEventBus
{
    private readonly ConcurrentDictionary<Guid, WorkspaceStream> _workspaces = new();

    public DomainEvent Publish(Guid workspaceId, Func<long, DateTimeOffset, DomainEvent> make)
    {
        var stream = Get(workspaceId);
        DomainEvent created;
        lock (stream.Gate)
        {
            var seq = stream.Seq + 1;
            created = make(seq, clock.GetUtcNow());
            if (created.Seq != seq)
            {
                throw new InvalidOperationException("An event must carry the sequence number the bus assigned.");
            }
            stream.Seq = seq;
            stream.Buffer.AddLast(created);
            while (stream.Buffer.Count > bufferSize)
            {
                stream.Buffer.RemoveFirst();
            }
        }
        foreach (var subscriber in stream.Subscribers.Values)
        {
            subscriber.Writer.TryWrite(created);
        }
        return created;
    }

    public async IAsyncEnumerable<DomainEvent> Subscribe(Guid workspaceId, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = Get(workspaceId);
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<DomainEvent>(new UnboundedChannelOptions { SingleReader = true });
        stream.Subscribers[id] = channel;
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await channel.Reader.WaitToReadAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                if (!more)
                {
                    yield break;
                }
                while (channel.Reader.TryRead(out var next))
                {
                    yield return next;
                }
            }
        }
        finally
        {
            stream.Subscribers.TryRemove(id, out _);
        }
    }

    public Replay ReplaySince(Guid workspaceId, long seq)
    {
        var stream = Get(workspaceId);
        lock (stream.Gate)
        {
            if (stream.Buffer.Count == 0)
            {
                return new Replay(false, []);
            }
            var oldest = stream.Buffer.First!.Value.Seq;
            if (seq + 1 < oldest)
            {
                return new Replay(true, []);
            }
            return new Replay(false, stream.Buffer.Where(e => e.Seq > seq).ToList());
        }
    }

    public long LastSeq(Guid workspaceId) => _workspaces.TryGetValue(workspaceId, out var s) ? Volatile.Read(ref s.Seq) : 0;

    private WorkspaceStream Get(Guid workspaceId) => _workspaces.GetOrAdd(workspaceId, _ => new WorkspaceStream());

    private sealed class WorkspaceStream
    {
        public long Seq;
        public readonly object Gate = new();
        public readonly LinkedList<DomainEvent> Buffer = new();
        public readonly ConcurrentDictionary<Guid, Channel<DomainEvent>> Subscribers = new();
    }
}
