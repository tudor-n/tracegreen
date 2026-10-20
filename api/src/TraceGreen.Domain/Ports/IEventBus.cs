using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Ports;

/// <summary><see cref="Gap"/> means the requested position is older than the buffer; the client must refetch a snapshot.</summary>
public sealed record Replay(bool Gap, IReadOnlyList<DomainEvent> Events);

/// <summary>
/// In-process today (one API instance); Postgres LISTEN/NOTIFY or a hosted realtime service later. The bus assigns the
/// sequence number and timestamp so publishers cannot disagree about ordering.
/// </summary>
public interface IEventBus
{
    DomainEvent Publish(Guid workspaceId, Func<long, DateTimeOffset, DomainEvent> make);

    IAsyncEnumerable<DomainEvent> Subscribe(Guid workspaceId, CancellationToken ct);

    Replay ReplaySince(Guid workspaceId, long seq);

    long LastSeq(Guid workspaceId);
}
