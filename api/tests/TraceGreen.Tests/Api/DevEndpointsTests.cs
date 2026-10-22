using System.Net;
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
}
