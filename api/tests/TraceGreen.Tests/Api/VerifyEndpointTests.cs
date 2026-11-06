using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;

namespace TraceGreen.Tests.Api;

/// <summary>
/// The supplier loop over HTTP (slice 3): request a link, read the context, submit, and every status of ruling 5.
/// Each test starts from the seed state through the reset route, since the class shares one host.
/// </summary>
public class VerifyEndpointTests : IClassFixture<VerifyWebAppFactory>
{
    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly Guid N3 = Guid.Parse("11111111-1111-4111-8111-000000000003");

    private readonly VerifyWebAppFactory _factory;
    private readonly HttpClient _client;

    public VerifyEndpointTests(VerifyWebAppFactory factory)
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

    private async Task<JsonElement> RequestLink(Guid nodeId)
    {
        var response = await _client.PostAsJsonAsync("/api/request-link", new { nodeId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private async Task<string> Token(Guid nodeId)
    {
        var url = new Uri((await RequestLink(nodeId)).GetProperty("url").GetString()!);
        return Uri.UnescapeDataString(url.Query["?t=".Length..]);
    }

    private Task<HttpResponseMessage> Context(string token) =>
        _client.GetAsync($"/api/verify/context?t={Uri.EscapeDataString(token)}", Ct);

    private Task<HttpResponseMessage> Submit(string token, string? mode = "road", string? energy = "grid", decimal? massT = null) =>
        _client.PostAsJsonAsync("/api/verify", new { t = token, transportMode = mode, energySource = energy, massT }, Ct);

    [Fact]
    public async Task RequestLinkReturnsAUrlOnThePageRouteWithExpiryAndLoopbackFlag()
    {
        await Reset();

        var body = await RequestLink(N3);

        var url = new Uri(body.GetProperty("url").GetString()!);
        Assert.Equal($"/verify/{N3:D}", url.AbsolutePath);
        Assert.StartsWith("?t=v1.", url.Query);
        Assert.Equal(N3, body.GetProperty("nodeId").GetGuid());
        // TestServer's host is localhost: a phone could not open this, so the QR card must warn.
        Assert.True(body.GetProperty("loopback").GetBoolean());
        var expected = _factory.Clock.GetUtcNow().AddDays(7);
        Assert.InRange(body.GetProperty("expiresAt").GetDateTimeOffset(), expected.AddSeconds(-1), expected);
    }

    [Fact]
    public async Task RequestLinkAppendsAnAuditEntryWithoutTheToken()
    {
        await Reset();

        var token = await Token(N3);

        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        var entry = audit[^1];
        Assert.Equal("link.requested", entry.Action);
        Assert.Equal(N3.ToString("D"), entry.Payload["nodeId"]!.GetValue<string>());
        var nonce = token.Split('.')[3];
        Assert.DoesNotContain(nonce, entry.Payload.ToJsonString());
        Assert.DoesNotContain(token.Split('.')[5], entry.Payload.ToJsonString());
    }

    [Fact]
    public async Task RequestLinkForAnUnknownNodeIs404()
    {
        await Reset();

        var response = await _client.PostAsJsonAsync("/api/request-link", new { nodeId = Guid.NewGuid() }, Ct);

        await AssertProblem(response, HttpStatusCode.NotFound, "NodeNotFound");
    }

    [Fact]
    public async Task ContextNamesTheItemAndPreselectsTheLaneDefaultAndGrid()
    {
        await Reset();
        var token = await Token(N3);

        var response = await Context(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(N3, body.GetProperty("nodeId").GetGuid());
        Assert.Equal(["road"], body.GetProperty("modes").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal("road", body.GetProperty("defaultMode").GetString());
        Assert.Equal(["grid", "renewable", "unknown"], body.GetProperty("energySources").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal("grid", body.GetProperty("defaultEnergy").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("supplierLabel").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("itemDescription").GetString()));
        // Nothing that could carry a credential or an address.
        var text = body.GetRawText();
        Assert.DoesNotContain(token, text);
        Assert.DoesNotContain("ipHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonce", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmittingBothAnswersVerifiesPublishesKpisAndKeepsTheChainValid()
    {
        await Reset();
        var token = await Token(N3);
        var before = Bus.LastSeq(Ids.DemoWorkspace);

        var response = await Submit(token, "road", "renewable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("VERIFIED", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("warnings").EnumerateArray());

        var node = await Repo.GetNodeAsync(Ctx, N3, Ct);
        Assert.Equal(NodeStatus.Verified, node!.Status);
        Assert.Equal(EnergySource.Renewable, node.EnergySource);
        Assert.NotNull(node.ActivityKg);

        var events = Bus.ReplaySince(Ids.DemoWorkspace, before).Events;
        var verified = Assert.Single(events.OfType<NodeVerified>());
        Assert.Equal(NodeStatus.Verified, verified.Node.Status);
        Assert.Equal(CarbonBasis.Activity, verified.Node.Basis);
        Assert.Equal(node.ActivityKg, verified.Node.CarbonKg);
        Assert.Equal(KpiOf(await Repo.ListNodesAsync(Ctx, Ct)), verified.Kpis);
        var appended = Assert.Single(events.OfType<AuditAppended>());
        Assert.True(verified.Seq < appended.Seq);
        Assert.Equal("verification.submitted", appended.Entry.Action);
        Assert.Equal("VERIFIED", appended.Entry.Payload["status"]!.GetValue<string>());

        var chain = await Json(await _client.PostAsync("/api/audit/verify", null, Ct));
        Assert.True(chain.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, chain.GetProperty("brokenAt").ValueKind);
        Assert.Equal((await Repo.ListAuditAsync(Ctx, 0, 1000, Ct)).Count, chain.GetProperty("length").GetInt32());
    }

    [Fact]
    public async Task SkippingOneQuestionMakesTheNodePartial()
    {
        await Reset();
        var token = await Token(N3);

        var response = await Submit(token, mode: "road", energy: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PARTIAL", (await Json(response)).GetProperty("status").GetString());
        var node = await Repo.GetNodeAsync(Ctx, N3, Ct);
        Assert.Equal(NodeStatus.Partial, node!.Status);
        Assert.Equal(node.SpendKg, node.CarbonKg);
    }

    [Fact]
    public async Task OmittedFieldsCountAsSkipped()
    {
        await Reset();
        var token = await Token(N3);

        var response = await _client.PostAsJsonAsync("/api/verify", new { t = token, energySource = "unknown" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PARTIAL", (await Json(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AReplayIs409AndTheContextThenSaysAlreadyUsed()
    {
        await Reset();
        var token = await Token(N3);
        Assert.Equal(HttpStatusCode.OK, (await Submit(token)).StatusCode);
        var seq = Bus.LastSeq(Ids.DemoWorkspace);

        await AssertProblem(await Submit(token), HttpStatusCode.Conflict, "AlreadyUsed");
        await AssertProblem(await Context(token), HttpStatusCode.Conflict, "AlreadyUsed");

        Assert.Single(await Repo.ListVerificationsAsync(Ctx, N3, Ct));
        Assert.Equal(seq, Bus.LastSeq(Ids.DemoWorkspace));
    }

    [Fact]
    public async Task ConcurrentSubmitsOfOneLinkVerifyOnce()
    {
        await Reset();
        var token = await Token(N3);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Submit(token)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await Repo.ListVerificationsAsync(Ctx, N3, Ct));
    }

    [Fact]
    public async Task AnExpiredLinkIs410()
    {
        await Reset();
        var token = await Token(N3);

        // The clock only moves forward; the other tests read it rather than assume the wall clock.
        _factory.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));

        await AssertProblem(await Context(token), HttpStatusCode.Gone, "LinkExpired");
        await AssertProblem(await Submit(token), HttpStatusCode.Gone, "LinkExpired");
        Assert.Empty(await Repo.ListVerificationsAsync(Ctx, N3, Ct));
    }

    [Fact]
    public async Task ALinkBoundToAnotherInvoiceVersionIs410()
    {
        await Reset();
        var signer = _factory.Services.GetRequiredService<ILinkSigner>();
        var stale = signer.Sign(new LinkClaims(N3, new string('a', 64), signer.MintNonce(), _factory.Clock.GetUtcNow().AddDays(1)));

        await AssertProblem(await Context(stale), HttpStatusCode.Gone, "InvoiceChanged");
        await AssertProblem(await Submit(stale), HttpStatusCode.Gone, "InvoiceChanged");
    }

    [Fact]
    public async Task AGenuineLinkForANodeTheResetRemovedIs410()
    {
        await Reset();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/ingest/demo", null, Ct)).StatusCode);
        var token = await Token(TestNodes.N1Id);
        await Reset();

        await AssertProblem(await Submit(token), HttpStatusCode.Gone, "InvoiceChanged");
    }

    [Fact]
    public async Task ATamperedSignatureIs401()
    {
        await Reset();
        var token = await Token(N3);
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        await AssertProblem(await Context(tampered), HttpStatusCode.Unauthorized, "InvalidToken");
        await AssertProblem(await Submit(tampered), HttpStatusCode.Unauthorized, "InvalidToken");
    }

    [Fact]
    public async Task AForgedTokenForAnUnknownNodeIs401NotGone()
    {
        await Reset();
        var token = await Token(N3);
        var forged = token.Replace(N3.ToString("D"), Guid.NewGuid().ToString("D"));

        await AssertProblem(await Submit(forged), HttpStatusCode.Unauthorized, "InvalidToken");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("v1.a.b.c.d.e")]
    public async Task AMalformedTokenIs401(string token) =>
        await AssertProblem(await Context(token), HttpStatusCode.Unauthorized, "InvalidToken");

    [Fact]
    public async Task AContextRequestWithoutATokenIs401() =>
        await AssertProblem(await _client.GetAsync("/api/verify/context", Ct), HttpStatusCode.Unauthorized, "InvalidToken");

    [Fact]
    public async Task AnAbsurdlyLongTokenIs401() =>
        await AssertProblem(await Submit(new string('v', 10_000)), HttpStatusCode.Unauthorized, "InvalidToken");

    [Theory]
    [InlineData("sea", "grid", null)]
    [InlineData(null, null, null)]
    [InlineData("road", "grid", 0.0)]
    [InlineData("road", "grid", -1.0)]
    public async Task AnUnacceptableAnswerIs422AndLeavesTheLinkUsable(string? mode, string? energy, double? massT)
    {
        await Reset();
        var token = await Token(N3);

        await AssertProblem(await Submit(token, mode, energy, (decimal?)massT), HttpStatusCode.UnprocessableEntity, "InvalidAnswer");

        Assert.Equal(HttpStatusCode.OK, (await Submit(token)).StatusCode);
    }

    [Fact]
    public async Task ConfirmingAirOnTheHeavyDemoInvoiceVerifiesWithAWarning()
    {
        await Reset();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/ingest/demo", null, Ct)).StatusCode);
        var token = await Token(TestNodes.N1Id);

        var body = await Json(await Submit(token, "air", "grid"));

        // D2: the warning is shown, the verification goes through.
        Assert.Equal("VERIFIED", body.GetProperty("status").GetString());
        Assert.Equal("AIR_HEAVY", Assert.Single(body.GetProperty("warnings").EnumerateArray()).GetProperty("code").GetString());
        var graph = await Json(await _client.GetAsync("/api/graph", Ct));
        var n1 = graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == TestNodes.N1Id);
        Assert.Equal("AIR_HEAVY", Assert.Single(n1.GetProperty("warnings").EnumerateArray()).GetProperty("code").GetString());
        Assert.Equal(TestNodes.N1().ActivityKg, n1.GetProperty("carbonKg").GetDecimal());
    }

    [Fact]
    public async Task ConfirmingSeaMovesCostAndLeadTimeToTheSeaLeg()
    {
        await Reset();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/ingest/demo", null, Ct)).StatusCode);
        var token = await Token(TestNodes.N1Id);

        var body = await Json(await Submit(token, "sea", "grid"));

        Assert.Empty(body.GetProperty("warnings").EnumerateArray());
        var node = await Repo.GetNodeAsync(Ctx, TestNodes.N1Id, Ct);
        // CN-RO sea leg (illustrative): 40 days, 0.3 EUR/kg on 2.3 t.
        Assert.Equal(40, node!.LeadDays);
        Assert.Equal(node.SpendEur + 2300m * 0.3m, node.CostEur);
    }

    [Fact]
    public async Task AConfirmedMassReplacesTheInvoiceMass()
    {
        await Reset();
        var token = await Token(N3);

        Assert.Equal(HttpStatusCode.OK, (await Submit(token, massT: 1.25m)).StatusCode);

        var node = await Repo.GetNodeAsync(Ctx, N3, Ct);
        Assert.Equal(1.25m, node!.MassT);
        Assert.Equal(1.25m, Assert.Single(await Repo.ListVerificationsAsync(Ctx, N3, Ct)).MassTConfirmed);
    }

    [Fact]
    public async Task AnAlreadyVerifiedNodeCanBeAnsweredAgainWithANewLink()
    {
        await Reset();
        var token = await Token(N2);

        Assert.Equal(HttpStatusCode.OK, (await Submit(token, "air", "unknown")).StatusCode);

        var node = await Repo.GetNodeAsync(Ctx, N2, Ct);
        Assert.Equal(TransportMode.Air, node!.TransportMode);
        Assert.Equal(2, (await Repo.ListVerificationsAsync(Ctx, N2, Ct)).Count);
    }

    [Fact]
    public async Task TheTokenNeverReachesALogLineOrAnAuditPayload()
    {
        await Reset();
        var token = await Token(N3);
        await Context(token);
        await Submit(token);
        await Submit(token);

        var signature = token.Split('.')[5];
        var nonce = token.Split('.')[3];
        Assert.NotEmpty(_factory.Logs.Messages);
        Assert.DoesNotContain(_factory.Logs.Messages, m => m.Contains(signature, StringComparison.Ordinal) || m.Contains(nonce, StringComparison.Ordinal));
        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        Assert.DoesNotContain(audit, a => a.Payload.ToJsonString().Contains(nonce, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SimulateAnswerGoesThroughTheSamePath()
    {
        await Reset();
        var before = (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct)).Count;

        var response = await _client.PostAsJsonAsync("/api/dev/simulate-answer", new { nodeId = N3, transportMode = "road", energySource = "grid" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("VERIFIED", (await Json(response)).GetProperty("status").GetString());
        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        Assert.Equal(["link.requested", "verification.submitted"], audit.Skip(before).Select(a => a.Action));
    }

    [Fact]
    public async Task SimulateAnswerForAnUnknownNodeIs404()
    {
        await Reset();

        var response = await _client.PostAsJsonAsync("/api/dev/simulate-answer", new { nodeId = Guid.NewGuid(), transportMode = "road" }, Ct);

        await AssertProblem(response, HttpStatusCode.NotFound, "NodeNotFound");
    }

    private static Kpis KpiOf(IReadOnlyList<SupplierNode> nodes) => TraceGreen.Domain.Carbon.KpiCalculator.Compute(nodes);
}

/// <summary>The 11th call in a minute from one address is refused; the bucket is the phone's address, not the proxy's.</summary>
public class VerifyRateLimitTests : IClassFixture<RateLimitedWebAppFactory>
{
    private readonly HttpClient _client;

    public VerifyRateLimitTests(RateLimitedWebAppFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> Call(string peer, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/verify/context?t=garbage");
        request.Headers.Add(RateLimitedWebAppFactory.PeerHeader, peer);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }
        return _client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task TheEleventhCallInAMinuteIs429()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Call("192.168.137.10")).StatusCode);
        }

        var response = await Call("192.168.137.10");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("RateLimited", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        // Another phone keeps its own allowance.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call("192.168.137.11")).StatusCode);
    }

    [Fact]
    public async Task APhoneCannotChooseItsBucketThroughXForwardedFor()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Call("192.168.137.20", $"10.0.0.{i}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Call("192.168.137.20", "10.0.0.99")).StatusCode);
    }

    [Fact]
    public async Task BehindATrustedProxyEachPhoneHasItsOwnBucket()
    {
        // Vite or nginx in Compose: one proxy address, the phone's address in X-Forwarded-For.
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Call("172.18.0.5", "192.168.137.30")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Call("172.18.0.5", "192.168.137.30")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call("172.18.0.5", "192.168.137.31")).StatusCode);
    }
}
