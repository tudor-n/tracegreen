using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Api.Endpoints;

/// <summary>
/// Server-Sent Events for the dashboard. Every message carries <c>id: seq</c>; on reconnect the browser sends
/// <c>Last-Event-ID</c> and missed events are replayed from the bus buffer, or a <c>resync</c> event tells the client
/// to refetch the snapshot. A <c>ping</c> every 20 s keeps proxies from closing the stream.
/// </summary>
public static class EventsEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    public static IEndpointRouteBuilder MapEvents(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (HttpContext http, IEventBus bus, SeedBundle seed, CancellationToken ct) =>
            {
                var workspaceId = seed.DemoState.WorkspaceId;
                long? since = long.TryParse(http.Request.Headers["Last-Event-ID"].FirstOrDefault(), out var parsed) ? parsed : null;
                http.Response.StatusCode = StatusCodes.Status200OK;
                http.Response.ContentType = "text/event-stream";
                http.Response.Headers.CacheControl = "no-cache, no-transform";
                http.Response.Headers["X-Accel-Buffering"] = "no";
                http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
                try
                {
                    await SseFormatter.WriteAsync(Stream(bus, workspaceId, since, ct), http.Response.Body, ct);
                }
                catch (OperationCanceledException)
                {
                    // The client closed the stream; nothing to report.
                }
                catch (IOException)
                {
                    // The client went away mid-write; the only IO failure a response body can have here.
                }
            })
            .WithName("GetEvents")
            .ExcludeFromDescription();
        return app;
    }

    public static string Serialize(DomainEvent e) => JsonSerializer.Serialize(e, e.GetType(), Json);

    private static async IAsyncEnumerable<SseItem<string>> Stream(
        IEventBus bus, Guid workspaceId, long? since, [EnumeratorCancellation] CancellationToken ct)
    {
        // Subscribe before replaying so nothing published in between is lost; the client ignores duplicate sequence numbers.
        var channel = Channel.CreateUnbounded<DomainEvent>(new UnboundedChannelOptions { SingleReader = true });
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in bus.Subscribe(workspaceId, ct))
                {
                    channel.Writer.TryWrite(e);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        yield return new SseItem<string>("{}", "hello")
        {
            EventId = bus.LastSeq(workspaceId).ToString(),
            ReconnectionInterval = TimeSpan.FromSeconds(2),
        };

        if (since is { } lastSeen)
        {
            var replay = bus.ReplaySince(workspaceId, lastSeen);
            if (replay.Gap)
            {
                yield return new SseItem<string>("{\"reason\":\"gap\"}", "resync");
            }
            else
            {
                foreach (var e in replay.Events)
                {
                    yield return Item(e);
                }
            }
        }

        while (!ct.IsCancellationRequested)
        {
            var read = channel.Reader.WaitToReadAsync(ct).AsTask();
            var winner = await Task.WhenAny(read, Task.Delay(Heartbeat, CancellationToken.None));
            if (ct.IsCancellationRequested)
            {
                yield break;
            }
            if (winner != read)
            {
                yield return new SseItem<string>("{}", "ping");
                continue;
            }
            bool more;
            try
            {
                more = await read;
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            if (!more)
            {
                yield break;
            }
            while (channel.Reader.TryRead(out var e))
            {
                yield return Item(e);
            }
        }
    }

    private static SseItem<string> Item(DomainEvent e) =>
        new(Serialize(e), e.Type) { EventId = e.Seq.ToString() };
}
