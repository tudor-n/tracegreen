using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

public class DevEndpointsTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DevEndpointsTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetReturns204AndPublishesGraphReset()
    {
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var client = _factory.CreateClient();
        var before = bus.LastSeq(Ids.DemoWorkspace);

        var response = await client.PostAsync("/api/dev/reset", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var replay = bus.ReplaySince(Ids.DemoWorkspace, before);
        Assert.Contains(replay.Events, e => e is GraphReset);
    }

    [Fact]
    public async Task ResetRestoresTheSeedStartState()
    {
        var repo = _factory.Services.GetRequiredService<IRepository>();
        var client = _factory.CreateClient();

        await client.PostAsync("/api/dev/reset", null, TestContext.Current.CancellationToken);

        var nodes = await repo.ListNodesAsync(new Ctx(Ids.DemoWorkspace, "test"), TestContext.Current.CancellationToken);
        Assert.Equal(4, nodes.Count);
        Assert.DoesNotContain(nodes, n => n.Id == TestNodes.N1Id);
    }
}

public class DevEndpointsDisabledTests : IClassFixture<NoDemoToolsWebAppFactory>
{
    private readonly NoDemoToolsWebAppFactory _factory;

    public DevEndpointsDisabledTests(NoDemoToolsWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ResetIsNotFoundWithoutDemoTools()
    {
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var client = _factory.CreateClient();
        var before = bus.LastSeq(Ids.DemoWorkspace);

        var response = await client.PostAsync("/api/dev/reset", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, bus.LastSeq(Ids.DemoWorkspace));
    }

    [Fact]
    public async Task SimulateAnswerIsNotFoundWithoutDemoTools()
    {
        var repo = _factory.Services.GetRequiredService<IRepository>();
        var client = _factory.CreateClient();
        var n3 = Guid.Parse("11111111-1111-4111-8111-000000000003");

        var response = await client.PostAsJsonAsync("/api/dev/simulate-answer", new { nodeId = n3, transportMode = "road" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await repo.ListVerificationsAsync(new Ctx(Ids.DemoWorkspace, "test"), n3, TestContext.Current.CancellationToken));
    }
}
