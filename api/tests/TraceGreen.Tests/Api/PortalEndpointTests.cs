using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;

namespace TraceGreen.Tests.Api;

/// <summary>
/// The supplier page over HTTP (F12): the link, the list, an answer, and every refusal. Each test starts from the seed
/// state through the reset route, since the class shares one host. In the seed, N3 (Oradea Harness SRL) is ESTIMATED,
/// N2 VERIFIED and N5 PARTIAL; <c>adversarial.xml</c> is a second invoice from N3's seller.
/// </summary>
public class PortalEndpointTests : IClassFixture<VerifyWebAppFactory>
{
    private static readonly Guid N1 = Guid.Parse("11111111-1111-4111-8111-000000000001");
    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly Guid N3 = Guid.Parse("11111111-1111-4111-8111-000000000003");
    private static readonly Guid N4 = Guid.Parse("11111111-1111-4111-8111-000000000004");
    private static readonly Guid N5 = Guid.Parse("11111111-1111-4111-8111-000000000005");

    private readonly VerifyWebAppFactory _factory;
    private readonly HttpClient _client;

    public PortalEndpointTests(VerifyWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IRepository Repo => _factory.Services.GetRequiredService<IRepository>();

    private IEventBus Bus => _factory.Services.GetRequiredService<IEventBus>();

    private static Ctx Ctx => new(Ids.DemoWorkspace, "test");

    private async Task Reset() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/dev/reset", null, Ct)).StatusCode);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }

    private async Task<JsonElement> RequestPortalLink(Guid nodeId)
    {
        var response = await _client.PostAsJsonAsync("/api/portal-link", new { nodeId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private async Task<string> Token(Guid nodeId)
    {
        var url = new Uri((await RequestPortalLink(nodeId)).GetProperty("url").GetString()!);
        return Uri.UnescapeDataString(url.Query["?t=".Length..]);
    }

    /// <summary>Imports N3's seller's second invoice and returns its node id.</summary>
    private async Task<Guid> ImportSecondOradeaInvoice()
    {
        var xml = await File.ReadAllTextAsync(TestPaths.Invoice("adversarial.xml"), Ct);
        var response = await _client.PostAsync("/api/ingest/ubl", new StringContent(xml, Encoding.UTF8, "text/xml"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("node").GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> View(string? token) =>
        _client.PostAsJsonAsync("/api/portal/view", new { t = token }, Ct);

    private async Task<JsonElement> ViewOk(string token)
    {
        var response = await View(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private Task<HttpResponseMessage> Answer(string? token, Guid nodeId, string? mode = "road", string? energy = "grid") =>
        _client.PostAsJsonAsync("/api/portal/answer", new { t = token, nodeId, transportMode = mode, energySource = energy }, Ct);

    private static JsonElement ItemOf(JsonElement view, Guid nodeId) =>
        view.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("nodeId").GetGuid() == nodeId);

    [Fact]
    public async Task PortalLinkPointsAtTheSupplierPageWithExpiryAndLoopbackFlag()
    {
        await Reset();

        var body = await RequestPortalLink(N3);

        var url = new Uri(body.GetProperty("url").GetString()!);
        Assert.Equal("/supplier", url.AbsolutePath);
        Assert.StartsWith("?t=p1.", url.Query);
        Assert.Equal(N3, body.GetProperty("nodeId").GetGuid());
        Assert.True(body.GetProperty("loopback").GetBoolean());
        var expected = _factory.Clock.GetUtcNow().AddDays(7);
        Assert.InRange(body.GetProperty("expiresAt").GetDateTimeOffset(), expected.AddSeconds(-1), expected);
    }

    [Fact]
    public async Task PortalLinkAppendsAnAuditEntryWithoutTheToken()
    {
        await Reset();

        var token = await Token(N3);

        var entry = (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct))[^1];
        Assert.Equal("portal.requested", entry.Action);
        Assert.Equal("buyer", entry.Actor);
        Assert.Equal(N3.ToString("D"), entry.Payload["nodeId"]!.GetValue<string>());
        Assert.DoesNotContain(token.Split('.')[3], entry.Payload.ToJsonString());
    }

    [Fact]
    public async Task PortalLinkForAnUnknownNodeIs404()
    {
        await Reset();

        var response = await _client.PostAsJsonAsync("/api/portal-link", new { nodeId = Guid.NewGuid() }, Ct);

        await AssertProblem(response, HttpStatusCode.NotFound, "NodeNotFound");
    }

    [Fact]
    public async Task ViewListsEveryDeliveryOfThatSupplierAndNoOtherSuppliers()
    {
        await Reset();
        var second = await ImportSecondOradeaInvoice();
        var token = await Token(N3);

        var view = await ViewOk(token);

        Assert.Equal("Oradea Harness SRL", view.GetProperty("supplierLabel").GetString());
        Assert.Equal("Transilvania Electro-Assembly SRL", view.GetProperty("buyerName").GetString());
        var ids = view.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nodeId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { N3, second }, ids);
    }

    [Fact]
    public async Task AnEstimatedDeliveryAsksWhatThePhonePageAsks()
    {
        await Reset();
        var token = await Token(N3);

        var item = Assert.Single((await ViewOk(token)).GetProperty("items").EnumerateArray());

        Assert.Equal("ESTIMATED", item.GetProperty("status").GetString());
        Assert.Equal("OH-2026-1005", item.GetProperty("invoiceNumber").GetString());
        Assert.Equal("2026-10-05", item.GetProperty("issueDate").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("itemDescription").GetString()));
        Assert.Equal(new[] { "road" }, item.GetProperty("modes").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal("road", item.GetProperty("defaultMode").GetString());
        Assert.Equal("grid", item.GetProperty("defaultEnergy").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("answeredMode").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("answeredEnergy").ValueKind);
    }

    [Fact]
    public async Task AnAnswerVerifiesTheDeliveryAndPublishesLikeThePhonePage()
    {
        await Reset();
        var token = await Token(N3);
        var before = Bus.LastSeq(Ids.DemoWorkspace);

        var response = await Answer(token, N3, "road", "renewable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("VERIFIED", body.GetProperty("outcome").GetProperty("status").GetString());
        Assert.Equal("VERIFIED", ItemOf(body.GetProperty("view"), N3).GetProperty("status").GetString());
        var node = await Repo.GetNodeAsync(Ctx, N3, Ct);
        Assert.Equal(NodeStatus.Verified, node!.Status);
        Assert.Equal(EnergySource.Renewable, node.EnergySource);
        var verified = Assert.Single(Bus.ReplaySince(Ids.DemoWorkspace, before).Events.OfType<NodeVerified>());
        Assert.Equal(N3, verified.Node.Id);
        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        Assert.Equal(new[] { "link.requested", "verification.submitted" }, audit.TakeLast(2).Select(a => a.Action));
        Assert.Equal("portal", audit[^2].Actor);
        Assert.Equal("supplier", audit[^1].Actor);
    }

    [Fact]
    public async Task TheLinkStaysUsableAndTheNextDeliveryPreselectsTheLastPowerSource()
    {
        await Reset();
        var second = await ImportSecondOradeaInvoice();
        var token = await Token(N3);
        var answered = await Answer(token, N3, "road", "renewable");
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        // The answer carries the list as it now is, the same list the page would read next.
        Assert.Equal((await ViewOk(token)).GetRawText(), (await Json(answered)).GetProperty("view").GetRawText());

        var view = await ViewOk(token);

        var items = view.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(second, items[0].GetProperty("nodeId").GetGuid());
        Assert.Equal("renewable", items[0].GetProperty("defaultEnergy").GetString());
        Assert.Equal("VERIFIED", items[1].GetProperty("status").GetString());
        Assert.Equal("road", items[1].GetProperty("answeredMode").GetString());
        Assert.Equal("renewable", items[1].GetProperty("answeredEnergy").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Answer(token, second, "road", "renewable")).StatusCode);
    }

    [Fact]
    public async Task APartialDeliveryShowsItsAnswerAndCanBeCompleted()
    {
        await Reset();
        var token = await Token(N5);

        var item = ItemOf(await ViewOk(token), N5);

        Assert.Equal("PARTIAL", item.GetProperty("status").GetString());
        var answeredMode = item.GetProperty("answeredMode");
        var answeredEnergy = item.GetProperty("answeredEnergy");
        Assert.True(answeredMode.ValueKind == JsonValueKind.Null ^ answeredEnergy.ValueKind == JsonValueKind.Null);
        if (answeredMode.ValueKind != JsonValueKind.Null)
        {
            Assert.Equal(answeredMode.GetString(), item.GetProperty("defaultMode").GetString());
        }
        else
        {
            Assert.Equal(answeredEnergy.GetString(), item.GetProperty("defaultEnergy").GetString());
        }

        var response = await Answer(token, N5, item.GetProperty("defaultMode").GetString(), item.GetProperty("defaultEnergy").GetString());

        Assert.Equal("VERIFIED", (await Json(response)).GetProperty("outcome").GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnotherSuppliersDeliveryIs404()
    {
        await Reset();
        var token = await Token(N3);

        await AssertProblem(await Answer(token, N4), HttpStatusCode.NotFound, "NodeNotFound");
        await AssertProblem(await Answer(token, Guid.NewGuid()), HttpStatusCode.NotFound, "NodeNotFound");
        Assert.Equal(NodeStatus.Estimated, (await Repo.GetNodeAsync(Ctx, N4, Ct))!.Status);
    }

    [Fact]
    public async Task AConfirmedDeliveryIs409AndStaysAsItWas()
    {
        await Reset();
        var token = await Token(N2);

        await AssertProblem(await Answer(token, N2, "sea", "renewable"), HttpStatusCode.Conflict, "AlreadyConfirmed");
        Assert.Equal(EnergySource.Grid, (await Repo.GetNodeAsync(Ctx, N2, Ct))!.EnergySource);
    }

    [Fact]
    public async Task ARefusedAnswerLeavesNoLinkBehind()
    {
        await Reset();
        var token = await Token(N3);
        var length = (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct)).Count;

        await AssertProblem(await Answer(token, N3, null, null), HttpStatusCode.UnprocessableEntity, "InvalidAnswer");
        await AssertProblem(await Answer(token, N3, "air", "grid"), HttpStatusCode.UnprocessableEntity, "InvalidAnswer");

        Assert.Equal(length, (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct)).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("p1.11111111-1111-4111-8111-000000000003.4102444800.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AnInvalidTokenIs401(string? token)
    {
        await Reset();

        await AssertProblem(await View(token), HttpStatusCode.Unauthorized, "InvalidToken");
        await AssertProblem(await Answer(token, N3), HttpStatusCode.Unauthorized, "InvalidToken");
    }

    [Fact]
    public async Task AVerificationLinkTokenIsRefused()
    {
        await Reset();
        var response = await _client.PostAsJsonAsync("/api/request-link", new { nodeId = N3 }, Ct);
        var url = new Uri((await Json(response)).GetProperty("url").GetString()!);
        var verifyToken = Uri.UnescapeDataString(url.Query["?t=".Length..]);

        await AssertProblem(await View(verifyToken), HttpStatusCode.Unauthorized, "InvalidToken");
    }

    [Fact]
    public async Task AnExpiredLinkIs410()
    {
        await Reset();
        var token = await Token(N3);
        _factory.Clock.Advance(TimeSpan.FromDays(8));

        await AssertProblem(await View(token), HttpStatusCode.Gone, "LinkExpired");
        await AssertProblem(await Answer(token, N3), HttpStatusCode.Gone, "LinkExpired");
    }

    [Fact]
    public async Task ALinkWhoseDeliveryWasResetAwayIs410()
    {
        await Reset();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/ingest/demo", null, Ct)).StatusCode);
        var token = await Token(N1);
        await Reset();

        await AssertProblem(await View(token), HttpStatusCode.Gone, "InvoiceChanged");
    }

    [Fact]
    public async Task TheTokenNeverReachesALogOrAResponseBody()
    {
        await Reset();
        var token = await Token(N3);

        var view = await (await View(token)).Content.ReadAsStringAsync(Ct);
        var answer = await (await Answer(token, N3)).Content.ReadAsStringAsync(Ct);

        var signature = token.Split('.')[3];
        Assert.DoesNotContain(signature, view);
        Assert.DoesNotContain(signature, answer);
        Assert.DoesNotContain(_factory.Logs.Messages, m => m.Contains(signature, StringComparison.Ordinal));
        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        Assert.DoesNotContain(audit, a => a.Payload.ToJsonString().Contains(signature, StringComparison.Ordinal));
    }
}

/// <summary>The supplier page has its own allowance per address, apart from the verify routes'.</summary>
public class PortalRateLimitTests : IClassFixture<RateLimitedWebAppFactory>
{
    private readonly HttpClient _client;

    public PortalRateLimitTests(RateLimitedWebAppFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string peer, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(RateLimitedWebAppFactory.PeerHeader, peer);
        return _client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task TheThirtyFirstCallInAMinuteIs429AndThePhonePageKeepsItsOwnAllowance()
    {
        // Three times the phone page's 10 a minute, since the page holds a list.
        const string peer = "192.168.137.30";
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "/api/portal/view", peer, new { t = "garbage" })).StatusCode);
        }

        var response = await Send(HttpMethod.Post, "/api/portal/answer", peer, new { t = "garbage", nodeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("RateLimited", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/verify/context?t=garbage", peer)).StatusCode);
    }
}
