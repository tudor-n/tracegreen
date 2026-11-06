using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Api;

/// <summary>
/// The trust model over HTTP (F7): evidence on a supplier answer, the buyer's records, a person's decision on a claim, the
/// target, and what reaches the node view, the KPIs and the audit log. Each test starts from the seed state.
/// </summary>
public class TrustEndpointTests : IClassFixture<VerifyWebAppFactory>
{
    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly Guid N3 = Guid.Parse("11111111-1111-4111-8111-000000000003");

    private readonly VerifyWebAppFactory _factory;
    private readonly HttpClient _client;

    public TrustEndpointTests(VerifyWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IRepository Repo => _factory.Services.GetRequiredService<IRepository>();

    private static Ctx Ctx => new(Ids.DemoWorkspace, "test");

    private string TermsSha => _factory.Services.GetRequiredService<SeedBundle>().SupplierTerms.Sha256;

    private async Task Reset() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/dev/reset", null, Ct)).StatusCode);

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }

    private async Task<string> Token(Guid nodeId)
    {
        var response = await _client.PostAsJsonAsync("/api/request-link", new { nodeId }, Ct);
        var url = new Uri((await Json(response)).GetProperty("url").GetString()!);
        return Uri.UnescapeDataString(url.Query["?t=".Length..]);
    }

    private async Task<HttpResponseMessage> Submit(Guid nodeId, string mode, string energy, object? evidence = null) =>
        await _client.PostAsJsonAsync(
            "/api/verify", new { t = await Token(nodeId), transportMode = mode, energySource = energy, acceptedTerms = TermsSha, evidence }, Ct);

    private async Task<JsonElement> Node(Guid nodeId)
    {
        var graph = await Json(await _client.GetAsync("/api/graph", Ct));
        return graph.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == nodeId);
    }

    private static JsonElement Claim(JsonElement node, string kind) =>
        node.GetProperty("trust").GetProperty("claims").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == kind);

    [Fact]
    public async Task AnUnbackedRenewableAnswerCountsAsGridAndShowsWhatIsAtRisk()
    {
        await Reset();

        Assert.Equal(HttpStatusCode.OK, (await Submit(N3, "road", "renewable")).StatusCode);

        var node = await Node(N3);
        var figures = node.GetProperty("figures");
        var claimed = figures.GetProperty("claimedKg").GetDecimal();
        var counted = figures.GetProperty("countedKg").GetDecimal();
        Assert.True(counted > claimed);
        Assert.Equal(counted, node.GetProperty("carbonKg").GetDecimal());
        Assert.Equal(counted - claimed, node.GetProperty("trust").GetProperty("kgAtRisk").GetDecimal());
        Assert.True(node.GetProperty("trust").GetProperty("needsReview").GetBoolean());
        var energy = Claim(node, "energy");
        Assert.Equal("unproven", energy.GetProperty("level").GetString());
        Assert.Equal("NO_CERTIFICATE", energy.GetProperty("reasons")[0].GetProperty("code").GetString());
        Assert.Equal("notNeeded", Claim(node, "mode").GetProperty("level").GetString());
        Assert.True(figures.GetProperty("target").GetProperty("illustrative").GetBoolean());

        var kpis = (await Json(await _client.GetAsync("/api/graph", Ct))).GetProperty("kpis");
        Assert.Equal(counted - claimed, kpis.GetProperty("kgAtRisk").GetDecimal());
    }

    [Fact]
    public async Task ACertificateIdBacksTheRenewableAnswerAndIsRecordedInTheAuditLog()
    {
        await Reset();

        var response = await Submit(N3, "road", "renewable",
            new { shippingDocType = "cmr", shippingDocId = "ro 4471", dispatchDate = "2026-10-20", energyCertType = "guaranteeOfOrigin", energyCertId = "GO-RO-2026-000123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = await Node(N3);
        Assert.Equal("documented", Claim(node, "energy").GetProperty("level").GetString());
        Assert.Equal(node.GetProperty("figures").GetProperty("claimedKg").GetDecimal(), node.GetProperty("carbonKg").GetDecimal());
        var evidence = node.GetProperty("evidence");
        Assert.Equal("cmr", evidence.GetProperty("shippingDocType").GetString());
        Assert.Equal("RO4471", evidence.GetProperty("shippingDocId").GetString());
        Assert.Equal("guaranteeOfOrigin", evidence.GetProperty("energyCertType").GetString());

        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        var entry = audit.Last(a => a.Action == "verification.submitted");
        Assert.Equal("RO4471", entry.Payload["shippingDocId"]!.GetValue<string>());
        Assert.Equal("GO-RO-2026-000123", entry.Payload["energyCertId"]!.GetValue<string>());
        Assert.Equal("moderate", entry.Payload["trustBand"]!.GetValue<string>());
        var verification = Assert.Single(await Repo.ListVerificationsAsync(Ctx, N3, Ct));
        Assert.Equal("RO4471", verification.Evidence!.ShippingDocId);
    }

    [Theory]
    [InlineData("road", "grid", "containerNumber", "CSQU3054384", null, null, "check digit")]
    [InlineData("road", "grid", "airWaybill", "02012345675", null, null, "does not go with road")]
    [InlineData("road", "grid", "cmr", null, null, null, "together")]
    [InlineData("road", "grid", null, null, "guaranteeOfOrigin", "GO-1234", "renewable")]
    [InlineData("road", "renewable", null, null, "guaranteeOfOrigin", "x", "certificate id")]
    public async Task EvidenceThatCannotBeTrueIsRefusedNamingTheField(
        string mode, string energy, string? docType, string? docId, string? certType, string? certId, string message)
    {
        await Reset();

        var response = await Submit(N3, mode, energy, new { shippingDocType = docType, shippingDocId = docId, energyCertType = certType, energyCertId = certId });

        await AssertProblem(response, HttpStatusCode.UnprocessableEntity, "InvalidEvidence");
        var refused = await Repo.GetNodeAsync(Ctx, N3, Ct);
        Assert.Equal(NodeStatus.Estimated, refused!.Status);
        Assert.Contains(message, (await Json(await Submit(N3, mode, energy, new { shippingDocType = docType, shippingDocId = docId, energyCertType = certType, energyCertId = certId })))
            .GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheSupplierPageSendsEvidenceAndRefusesProofThatCannotBeRight()
    {
        await Reset();
        var link = await Json(await _client.PostAsJsonAsync("/api/portal-link", new { nodeId = N3 }, Ct));
        var portal = Uri.UnescapeDataString(new Uri(link.GetProperty("url").GetString()!).Query["?t=".Length..]);

        var refused = await _client.PostAsJsonAsync("/api/portal/answer", new
        {
            t = portal, nodeId = N3, transportMode = "road", energySource = "grid", acceptedTerms = TermsSha,
            evidence = new { shippingDocType = "containerNumber", shippingDocId = "CSQU3054384" },
        }, Ct);
        await AssertProblem(refused, HttpStatusCode.UnprocessableEntity, "InvalidEvidence");
        Assert.Equal(NodeStatus.Estimated, (await Repo.GetNodeAsync(Ctx, N3, Ct))!.Status);

        var answered = await _client.PostAsJsonAsync("/api/portal/answer", new
        {
            t = portal, nodeId = N3, transportMode = "road", energySource = "grid", acceptedTerms = TermsSha,
            evidence = new { shippingDocType = "containerNumber", shippingDocId = "CSQU 305438 3" },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var node = await Node(N3);
        Assert.Equal("CSQU3054383", node.GetProperty("evidence").GetProperty("shippingDocId").GetString());
        // Road on a road lane lowers nothing, so the document is noted but nothing needed backing.
        Assert.Equal("notNeeded", Claim(node, "mode").GetProperty("level").GetString());
    }

    [Fact]
    public async Task TheBuyersCustomsRecordCorroboratesTheModeAndIsAudited()
    {
        await Reset();

        var response = await _client.PostAsJsonAsync($"/api/nodes/{N2}/receipt",
            new { receivedOn = "2026-11-01", customsBorderMode = "sea", customsMrn = "26ro 1234567890abcd" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = await Json(response);
        Assert.Equal("26RO1234567890ABCD", node.GetProperty("evidence").GetProperty("customsMrn").GetString());
        Assert.Equal("2026-11-01", node.GetProperty("evidence").GetProperty("receivedOn").GetString());
        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        Assert.Equal("receipt.recorded", audit[^1].Action);
        Assert.Equal("sea", audit[^1].Payload["customsBorderMode"]!.GetValue<string>());
        // N2's sea answer lowers nothing on its sea lane, so it needed no proof, and the customs record agrees.
        Assert.Contains(Claim(node, "mode").GetProperty("reasons").EnumerateArray(), r => r.GetProperty("code").GetString() == "CUSTOMS_AGREES");
    }

    [Theory]
    [InlineData("2099-01-01", null, "future")]
    [InlineData(null, "12AB", "MRN")]
    public async Task ABuyerRecordThatCannotBeTrueIsRefused(string? receivedOn, string? mrn, string message)
    {
        await Reset();

        var response = await _client.PostAsJsonAsync($"/api/nodes/{N2}/receipt", new { receivedOn, customsMrn = mrn }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("InvalidRecord", problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APersonAcceptsAClaimNamingTheValueThePageShowed()
    {
        await Reset();
        await Submit(N3, "road", "renewable");

        var stale = await _client.PostAsJsonAsync($"/api/nodes/{N3}/claims/energy", new { decision = "accepted", expectedValue = "grid" }, Ct);
        await AssertProblem(stale, HttpStatusCode.Conflict, "ClaimChanged");
        var none = await _client.PostAsJsonAsync($"/api/nodes/{N3}/claims/mass", new { decision = "accepted", expectedValue = "1" }, Ct);
        await AssertProblem(none, HttpStatusCode.UnprocessableEntity, "NoClaim");
        var unknown = await _client.PostAsJsonAsync($"/api/nodes/{Guid.NewGuid()}/claims/energy", new { decision = "accepted", expectedValue = "renewable" }, Ct);
        await AssertProblem(unknown, HttpStatusCode.NotFound, "NodeNotFound");

        var response = await _client.PostAsJsonAsync($"/api/nodes/{N3}/claims/energy", new { decision = "accepted", expectedValue = "renewable" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = await Json(response);
        Assert.Equal("reviewed", Claim(node, "energy").GetProperty("level").GetString());
        Assert.Equal("accepted", Claim(node, "energy").GetProperty("review").GetString());
        Assert.Equal(node.GetProperty("figures").GetProperty("claimedKg").GetDecimal(), node.GetProperty("carbonKg").GetDecimal());
        Assert.Equal(0m, node.GetProperty("trust").GetProperty("kgAtRisk").GetDecimal());
        var audit = await Repo.ListAuditAsync(Ctx, 0, 1000, Ct);
        Assert.Equal("claim.reviewed", audit[^1].Action);
        Assert.Equal("unproven", audit[^1].Payload["fromLevel"]!.GetValue<string>());
    }

    [Fact]
    public async Task EveryDecisionAndRecordMovesTheTotalByExactlyWhatItMovesTheSupplier()
    {
        await Reset();
        Assert.Equal(HttpStatusCode.OK, (await Submit(N3, "road", "renewable")).StatusCode);

        async Task<(decimal Total, decimal Sum, decimal N3)> Totals()
        {
            var graph = await Json(await _client.GetAsync("/api/graph", Ct));
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToList();
            return (
                graph.GetProperty("kpis").GetProperty("totalKg").GetDecimal(),
                nodes.Sum(n => n.GetProperty("carbonKg").GetDecimal()),
                nodes.Single(n => n.GetProperty("id").GetGuid() == N3).GetProperty("carbonKg").GetDecimal());
        }

        var before = await Totals();
        Assert.Equal(before.Total, before.Sum);
        foreach (var decision in new[] { "accepted", "rejected", "accepted" })
        {
            await _client.PostAsJsonAsync($"/api/nodes/{N3}/claims/energy", new { decision, expectedValue = "renewable" }, Ct);
            var after = await Totals();
            Assert.Equal(after.Total, after.Sum);
            Assert.Equal(after.N3 - before.N3, after.Total - before.Total);
            Assert.Equal(after.N3, (await Node(N3)).GetProperty("figures").GetProperty("countedKg").GetDecimal());
            before = after;
        }
    }

    [Fact]
    public async Task NewRecordsReplaceADecisionMadeWithoutThem()
    {
        await Reset();
        // A person rejects N3's road answer, then the buyer records the customs declaration: the decision was made without
        // that record, so it is dropped and the record decides.
        await Submit(N3, "road", "renewable");
        await _client.PostAsJsonAsync($"/api/nodes/{N3}/claims/mode", new { decision = "rejected", expectedValue = "road" }, Ct);
        Assert.Equal("contradicted", Claim(await Node(N3), "mode").GetProperty("level").GetString());

        var recorded = await Json(await _client.PostAsJsonAsync($"/api/nodes/{N3}/receipt", new { customsBorderMode = "road" }, Ct));

        var mode = Claim(recorded, "mode");
        Assert.Equal(JsonValueKind.Null, mode.GetProperty("review").ValueKind);
        Assert.NotEqual("contradicted", mode.GetProperty("level").GetString());
    }

    [Fact]
    public async Task TheBuyerSetsAndClearsTheTarget()
    {
        await Reset();

        var set = await _client.PutAsJsonAsync($"/api/nodes/{N2}/target", new { mode = "air", energy = "grid" }, Ct);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var target = (await Json(set)).GetProperty("figures").GetProperty("target");
        Assert.Equal("air", target.GetProperty("mode").GetString());
        Assert.False(target.GetProperty("isDefault").GetBoolean());

        var road = await _client.PutAsJsonAsync($"/api/nodes/{N2}/target", new { mode = "road" }, Ct);
        await AssertProblem(road, HttpStatusCode.UnprocessableEntity, "ModeNotAvailable");

        var cleared = await _client.PutAsJsonAsync($"/api/nodes/{N2}/target", new { }, Ct);
        Assert.True((await Json(cleared)).GetProperty("figures").GetProperty("target").GetProperty("isDefault").GetBoolean());
        Assert.Equal("target.set", (await Repo.ListAuditAsync(Ctx, 0, 1000, Ct))[^1].Action);
    }
}
