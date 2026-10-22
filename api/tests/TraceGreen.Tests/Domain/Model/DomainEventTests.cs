using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TraceGreen.Api.Endpoints;
using TraceGreen.Domain.Model;

namespace TraceGreen.Tests.Domain.Model;

public class DomainEventTests
{
    [Fact]
    public void EveryConcreteEventHasADiscriminator()
    {
        var registered = typeof(DomainEvent).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(a => a.DerivedType).ToHashSet();
        var concrete = typeof(DomainEvent).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(DomainEvent)) && !t.IsAbstract);

        Assert.All(concrete, t => Assert.Contains(t, registered));
    }

    [Fact]
    public void WireTypeLeadsAndMatchesTheSseEventName()
    {
        var events = new DomainEvent[]
        {
            new GraphReset(7, DateTimeOffset.UnixEpoch, Ids.DemoWorkspace),
            new AuditAppended(8, DateTimeOffset.UnixEpoch, Ids.DemoWorkspace, new AuditEntry(
                1, Ids.DemoWorkspace, DateTimeOffset.UnixEpoch, "seed", "seed.loaded", new JsonObject(), new string('0', 64), new string('a', 64))),
        };

        foreach (var e in events)
        {
            var json = EventsEndpoints.Serialize(e);
            using var doc = JsonDocument.Parse(json);
            var first = doc.RootElement.EnumerateObject().First();
            Assert.Equal("type", first.Name);
            Assert.Equal(e.Type, first.Value.GetString());
            Assert.Equal(e.Seq, doc.RootElement.GetProperty("seq").GetInt64());
        }
        Assert.Equal("graph.reset", events[0].Type);
        Assert.Equal("audit.appended", events[1].Type);
    }

    [Fact]
    public void AnEventThatCannotBeSerialisedBecomesAResyncInsteadOfBreakingTheStream()
    {
        var broken = new AuditAppended(9, DateTimeOffset.UnixEpoch, Ids.DemoWorkspace, new AuditEntry(
            1, Ids.DemoWorkspace, DateTimeOffset.UnixEpoch, null!, "seed.loaded", new JsonObject(), new string('0', 64), new string('a', 64)));

        var item = EventsEndpoints.Item(broken);

        Assert.Equal("resync", item.EventType);
        Assert.Equal("9", item.EventId);
    }
}
