using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

public class GraphEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public GraphEndpointTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<JsonElement> Snapshot()
    {
        var client = _factory.CreateClient();
        return await client.GetFromJsonAsync<JsonElement>("/api/graph", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SnapshotHoldsTheFourSeededNodesWithKpisAndMode()
    {
        var body = await Snapshot();

        Assert.Equal("memory", body.GetProperty("mode").GetString());
        Assert.Equal("Transilvania Electro-Assembly SRL", body.GetProperty("buyer").GetProperty("name").GetString());
        var statuses = body.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("status").GetString()).Order().ToList();
        Assert.Equal(["ESTIMATED", "ESTIMATED", "PARTIAL", "VERIFIED"], statuses);
        var kpis = body.GetProperty("kpis");
        Assert.Equal(4, kpis.GetProperty("nodeCount").GetInt32());
        Assert.Equal(1, kpis.GetProperty("verifiedCount").GetInt32());
        Assert.True(kpis.GetProperty("totalKg").GetDecimal() > 0m);
        Assert.InRange(kpis.GetProperty("primaryShare").GetDouble(), 0.0001, 0.9999);
    }

    [Fact]
    public async Task NodesCarryTheReadModelTheSpaRendersWithoutComputing()
    {
        var body = await Snapshot();
        var n2 = body.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("status").GetString() == "VERIFIED");

        Assert.Equal("activity", n2.GetProperty("basis").GetString());
        Assert.Equal("low", n2.GetProperty("uncertainty").GetString());
        Assert.Equal(n2.GetProperty("activity").GetProperty("kg").GetDecimal(), n2.GetProperty("carbonKg").GetDecimal());
        Assert.Equal(2, n2.GetProperty("activity").GetProperty("factors").GetArrayLength());
        Assert.Contains("PLACEHOLDER", n2.GetProperty("activity").GetProperty("factors")[0].GetProperty("source").GetString());
        Assert.Equal(3, n2.GetProperty("spend").GetProperty("factors").GetArrayLength());
        Assert.True(n2.GetProperty("lane").GetProperty("illustrative").GetBoolean());
        Assert.Equal(JsonValueKind.Number, n2.GetProperty("costEur").ValueKind);
    }

    [Fact]
    public async Task SnapshotIncludesTheSeedAuditChain()
    {
        var body = await Snapshot();
        var audit = body.GetProperty("audit").EnumerateArray().ToList();

        Assert.NotEmpty(audit);
        Assert.Equal(new string('0', 64), audit[0].GetProperty("prevHash").GetString());
        Assert.Equal("seed.loaded", audit[0].GetProperty("action").GetString());
    }

    [Fact]
    public async Task LastSeqIsTheBusPosition()
    {
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        bus.Publish(Ids.DemoWorkspace, (seq, ts) => new GraphReset(seq, ts, Ids.DemoWorkspace));
        var expected = bus.LastSeq(Ids.DemoWorkspace);

        var body = await Snapshot();

        Assert.True(body.GetProperty("lastSeq").GetInt64() >= expected);
    }
}
