using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

public class EventsEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public EventsEndpointTests(TestWebAppFactory factory) => _factory = factory;

    private static async Task<List<string>> ReadUntil(StreamReader reader, Func<List<string>, bool> done, TimeSpan timeout)
    {
        var lines = new List<string>();
        using var cts = new CancellationTokenSource(timeout);
        while (!done(lines))
        {
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new EndOfStreamException();
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>True once a whole message with the given event name (terminated by a blank line) has been read.</summary>
    private static bool MessageSeen(List<string> lines, string eventName)
    {
        var start = lines.FindIndex(l => l == $"event: {eventName}");
        return start >= 0 && lines.FindIndex(start, l => l.Length == 0) >= 0;
    }

    [Fact]
    public async Task StreamOpensWithRetryAndHelloThenDeliversPublishedEvents()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        var head = await ReadUntil(reader, l => MessageSeen(l, "hello"), TimeSpan.FromSeconds(10));
        Assert.Contains(head, l => l == "retry: 2000");

        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var published = bus.Publish(Ids.DemoWorkspace, (seq, ts) => new GraphReset(seq, ts, Ids.DemoWorkspace));

        var lines = await ReadUntil(reader, l => MessageSeen(l, "graph.reset"), TimeSpan.FromSeconds(10));
        Assert.Contains(lines, l => l == $"id: {published.Seq}");
        Assert.Contains(lines, l => l.StartsWith("data: ") && l.Contains("\"type\":\"graph.reset\""));
    }

    [Fact]
    public async Task LastEventIdReplaysBufferedEvents()
    {
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var before = bus.LastSeq(Ids.DemoWorkspace);
        var published = bus.Publish(Ids.DemoWorkspace, (seq, ts) => new GraphReset(seq, ts, Ids.DemoWorkspace));

        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("Last-Event-ID", before.ToString());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        var lines = await ReadUntil(reader, l => MessageSeen(l, "graph.reset"), TimeSpan.FromSeconds(10));
        Assert.Contains(lines, l => l == $"id: {published.Seq}");
    }

    [Fact]
    public async Task AncientLastEventIdTriggersResync()
    {
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        for (var i = 0; i < 600; i++)
        {
            bus.Publish(Ids.DemoWorkspace, (seq, ts) => new GraphReset(seq, ts, Ids.DemoWorkspace));
        }

        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("Last-Event-ID", "1");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        var lines = await ReadUntil(reader, l => MessageSeen(l, "resync"), TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(lines, l => l.StartsWith("event: graph.reset"));
    }
}
