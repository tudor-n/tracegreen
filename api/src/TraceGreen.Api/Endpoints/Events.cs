using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
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
            .WithTags("Events")
            .WithSummary("Server-Sent Events: each domain event's data is a DomainEvent; control events hello, resync and ping carry no domain data")
            // Described (not excluded) so the event records reach the generated client types like any response body.
            .Produces<DomainEvent>(StatusCodes.Status200OK, "text/event-stream");
        return app;
    }

    /// <summary>The serializer always writes the discriminator, so each event schema lists <c>type</c> as required.</summary>
    public static Task RequireEventType(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        if (context.JsonTypeInfo.Type.IsSubclassOf(typeof(DomainEvent)) && schema.Properties?.ContainsKey("type") == true)
        {
            schema.Required ??= new HashSet<string>();
            schema.Required.Add("type");
        }
        return Task.CompletedTask;
    }

    /// <summary>Serialised as the base type so the <c>type</c> discriminator leads, matching the <c>DomainEvent</c> OpenAPI component.</summary>
    public static string Serialize(DomainEvent e) => JsonSerializer.Serialize(e, ApiJson.Options);

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

    /// <summary>
    /// An event that cannot be serialised (a null where the contract promises a value) becomes a <c>resync</c>, so the
    /// client refetches the snapshot instead of the stream dying on it now and on every later replay.
    /// </summary>
    public static SseItem<string> Item(DomainEvent e)
    {
        try
        {
            return new SseItem<string>(Serialize(e), e.Type) { EventId = e.Seq.ToString() };
        }
        catch (JsonException)
        {
            return new SseItem<string>("{\"reason\":\"unserializable\"}", "resync") { EventId = e.Seq.ToString() };
        }
    }
}
