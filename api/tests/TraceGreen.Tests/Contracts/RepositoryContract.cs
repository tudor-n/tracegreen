using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Tests.Contracts;

/// <summary>Behaviour every repository adapter must satisfy. Subclasses supply a freshly seeded instance.</summary>
public abstract class RepositoryContract
{
    protected static readonly Ctx Ctx = new(Ids.DemoWorkspace, "test");
    protected static readonly SeedBundle Bundle = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid N2 = Guid.Parse("11111111-1111-4111-8111-000000000002");
    private static readonly Guid N3 = Guid.Parse("11111111-1111-4111-8111-000000000003");
    private static readonly Guid N5 = Guid.Parse("11111111-1111-4111-8111-000000000005");
    private static readonly Guid N1 = Guid.Parse("11111111-1111-4111-8111-000000000001");

    /// <summary>A repository seeded with <c>reset: true</c>.</summary>
    protected abstract Task<IRepository> MakeAsync();

    private static AuditBuilder Audit(string action, JsonNode? payload = null) =>
        prev => AuditChain.Build(prev, Ids.DemoWorkspace, Now, "test", action, payload ?? new JsonObject());

    private static Verification NewVerification(Guid nodeId, string nonce) => new(
        Ids.NewId(), Ids.DemoWorkspace, nodeId, nonce, TransportMode.Road, EnergySource.Grid, 1.4m, Now, "iphash", Now);

    private static readonly NodePatch VerifiedPatch = new(NodeStatus.Verified, 123m, TransportMode.Road, EnergySource.Grid, 1.4m);

    private static async Task<IReadOnlyList<AuditEntry>> AllAudit(IRepository repo) =>
        await repo.ListAuditAsync(Ctx, 0, 10_000, CancellationToken.None);

    [Fact]
    public async Task SeedResetLoadsDemoState()
    {
        var repo = await MakeAsync();

        var nodes = await repo.ListNodesAsync(Ctx, CancellationToken.None);
        Assert.Equal(4, nodes.Count);
        Assert.DoesNotContain(nodes, n => n.Id == N1);
        Assert.Equal(NodeStatus.Verified, nodes.Single(n => n.Id == N2).Status);
        Assert.Equal(NodeStatus.Partial, nodes.Single(n => n.Id == N5).Status);
        Assert.Equal(NodeStatus.Estimated, nodes.Single(n => n.Id == N3).Status);
        Assert.All(nodes, n => Assert.True(n.SpendKg > 0));
        Assert.True(nodes.Single(n => n.Id == N2).ActivityKg > 0);

        var audit = await AllAudit(repo);
        Assert.True(audit.Count >= 3);
        Assert.True(AuditChain.Verify(audit).Valid);

        Assert.True((await repo.ListFactorsAsync(CancellationToken.None)).Count > 10);
        Assert.Equal(7, (await repo.ListLanesAsync(CancellationToken.None)).Count);
        Assert.Equal(2, (await repo.ListAlternativesAsync(CancellationToken.None)).Count);
        Assert.Equal("RO99999999", (await repo.GetBuyerAsync(Ctx, CancellationToken.None)).Cui);
        Assert.Single(await repo.ListVerificationsAsync(Ctx, N2, CancellationToken.None));
    }

    [Fact]
    public async Task SeedTwiceWithoutResetDoesNotDuplicate()
    {
        var repo = await MakeAsync();
        var nodesBefore = (await repo.ListNodesAsync(Ctx, CancellationToken.None)).Count;
        var auditBefore = (await AllAudit(repo)).Count;

        await repo.SeedAsync(Bundle, reset: false, CancellationToken.None);

        Assert.Equal(nodesBefore, (await repo.ListNodesAsync(Ctx, CancellationToken.None)).Count);
        Assert.Equal(auditBefore, (await AllAudit(repo)).Count);
        Assert.Equal(Bundle.Factors.Count, (await repo.ListFactorsAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task InsertInvoiceWithNodeStoresBothAndLinksAuditToPreviousHash()
    {
        var repo = await MakeAsync();
        var before = await AllAudit(repo);
        var invoiceId = Ids.NewId();
        var nodeId = Ids.NewId();
        var invoice = new Invoice(invoiceId, Ids.DemoWorkspace, "Vendor", "RO1", "RO99999999", new DateOnly(2026, 10, 1), "EUR", 100m,
            "raw" + invoiceId.ToString("N"), "inv" + invoiceId.ToString("N"),
            new ExtractedInvoice("X-1", new DateOnly(2026, 10, 1), "EUR", 100m, new Party("Vendor", "RO1", "RO"), new Party("Buyer", "RO99999999", "RO"),
                [new ExtractedLine("1", "Thing", 1m, "KGM", 100m, null, null, null)]),
            Now);
        var node = TestNodes.Simple("Vendor", NodeStatus.Estimated, 40m, null) with { Id = nodeId, InvoiceId = invoiceId };

        var result = await repo.InsertInvoiceWithNodeAsync(Ctx, invoice, node, Audit("invoice.imported"), CancellationToken.None);
        Assert.Equal(InsertInvoiceStatus.Ok, result.Status);
        var entry = result.Entry!;

        Assert.NotNull(await repo.GetInvoiceAsync(Ctx, invoiceId, CancellationToken.None));
        Assert.NotNull(await repo.GetInvoiceByRawHashAsync(Ctx, invoice.RawXmlHash, CancellationToken.None));
        Assert.Equal(nodeId, (await repo.GetNodeAsync(Ctx, nodeId, CancellationToken.None))!.Id);
        Assert.Equal("invoice.imported", entry.Action);
        Assert.Equal(before[^1].Hash, entry.PrevHash);
        var after = await AllAudit(repo);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.True(AuditChain.Verify(after).Valid);
    }

    private static (Invoice Invoice, SupplierNode Node) NewImport(string rawHash)
    {
        var invoiceId = Ids.NewId();
        var invoice = new Invoice(invoiceId, Ids.DemoWorkspace, "Vendor", "RO1", "RO99999999", new DateOnly(2026, 10, 1), "EUR", 100m,
            rawHash, "inv" + invoiceId.ToString("N"),
            new ExtractedInvoice("X-1", new DateOnly(2026, 10, 1), "EUR", 100m, new Party("Vendor", "RO1", "RO"), new Party("Buyer", "RO99999999", "RO"),
                [new ExtractedLine("1", "Thing", 1m, "KGM", 100m, null, null, null)]),
            Now);
        return (invoice, TestNodes.Simple("Vendor", NodeStatus.Estimated, 40m, null) with { Id = Ids.NewId(), InvoiceId = invoiceId });
    }

    [Fact]
    public async Task InsertRefusesTheSameRawHashAndNamesTheNodeThatHasIt()
    {
        var repo = await MakeAsync();
        var rawHash = "raw" + Ids.NewId().ToString("N");
        var (first, firstNode) = NewImport(rawHash);
        var (second, secondNode) = NewImport(rawHash);
        await repo.InsertInvoiceWithNodeAsync(Ctx, first, firstNode, Audit("invoice.imported"), CancellationToken.None);
        var auditBefore = (await AllAudit(repo)).Count;

        var result = await repo.InsertInvoiceWithNodeAsync(Ctx, second, secondNode, Audit("invoice.imported"), CancellationToken.None);

        Assert.Equal(InsertInvoiceStatus.Duplicate, result.Status);
        Assert.Equal(firstNode.Id, result.ExistingNodeId);
        Assert.Null(result.Entry);
        Assert.Null(await repo.GetNodeAsync(Ctx, secondNode.Id, CancellationToken.None));
        Assert.Equal(auditBefore, (await AllAudit(repo)).Count);
    }

    [Fact]
    public async Task InsertRefusesANodeIdThatIsAlreadyTaken()
    {
        var repo = await MakeAsync();
        var (invoice, node) = NewImport("raw" + Ids.NewId().ToString("N"));

        var result = await repo.InsertInvoiceWithNodeAsync(Ctx, invoice, node with { Id = N2 }, Audit("invoice.imported"), CancellationToken.None);

        Assert.Equal(InsertInvoiceStatus.Duplicate, result.Status);
        Assert.Equal(N2, result.ExistingNodeId);
        Assert.Null(await repo.GetInvoiceAsync(Ctx, invoice.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RecordVerificationOkPatchesNodeStoresVerificationAppendsAudit()
    {
        var repo = await MakeAsync();
        var auditBefore = (await AllAudit(repo)).Count;

        var result = await repo.RecordVerificationAsync(Ctx, NewVerification(N3, "fresh-nonce-1"), VerifiedPatch, Audit("verification.submitted"), CancellationToken.None);

        Assert.Equal(RecordVerificationStatus.Ok, result.Status);
        Assert.Equal(NodeStatus.Verified, result.Node!.Status);
        Assert.Equal(123m, result.Node.ActivityKg);
        var stored = await repo.GetNodeAsync(Ctx, N3, CancellationToken.None);
        Assert.Equal(NodeStatus.Verified, stored!.Status);
        Assert.Equal(TransportMode.Road, stored.TransportMode);
        Assert.Single(await repo.ListVerificationsAsync(Ctx, N3, CancellationToken.None));
        var audit = await AllAudit(repo);
        Assert.Equal(auditBefore + 1, audit.Count);
        Assert.True(AuditChain.Verify(audit).Valid);
    }

    [Fact]
    public async Task RecordVerificationWithUsedNonceReturnsAlreadyUsedAndChangesNothing()
    {
        var repo = await MakeAsync();
        var auditBefore = (await AllAudit(repo)).Count;
        var usedNonce = Bundle.DemoState.Nodes.Single(n => n.Key == "N2").Nonce!;

        var result = await repo.RecordVerificationAsync(Ctx, NewVerification(N3, usedNonce), VerifiedPatch, Audit("verification.submitted"), CancellationToken.None);

        Assert.Equal(RecordVerificationStatus.AlreadyUsed, result.Status);
        Assert.Null(result.Node);
        Assert.Equal(NodeStatus.Estimated, (await repo.GetNodeAsync(Ctx, N3, CancellationToken.None))!.Status);
        Assert.Equal(auditBefore, (await AllAudit(repo)).Count);
    }

    [Fact]
    public async Task TenConcurrentRecordVerificationsWithSameNonceExactlyOneOk()
    {
        var repo = await MakeAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => repo.RecordVerificationAsync(Ctx, NewVerification(N3, "race-nonce"), VerifiedPatch, Audit("verification.submitted"), CancellationToken.None))));

        Assert.Equal(1, results.Count(r => r.Status == RecordVerificationStatus.Ok));
        Assert.Equal(9, results.Count(r => r.Status == RecordVerificationStatus.AlreadyUsed));
        Assert.True(AuditChain.Verify(await AllAudit(repo)).Valid);
    }

    [Fact]
    public async Task TwentyParallelAppendAuditNeverFork()
    {
        var repo = await MakeAsync();
        var before = (await AllAudit(repo)).Count;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => repo.AppendAuditAsync(Ctx, Audit("parallel", new JsonObject { ["i"] = i }), CancellationToken.None))));

        var audit = await AllAudit(repo);
        Assert.Equal(before + 20, audit.Count);
        Assert.True(AuditChain.Verify(audit).Valid);
    }

    [Fact]
    public async Task ListAuditPaginatesAscending()
    {
        var repo = await MakeAsync();
        var all = await AllAudit(repo);

        var firstTwo = await repo.ListAuditAsync(Ctx, 0, 2, CancellationToken.None);
        var rest = await repo.ListAuditAsync(Ctx, firstTwo[^1].Id, 10_000, CancellationToken.None);

        Assert.Equal(2, firstTwo.Count);
        Assert.True(firstTwo[0].Id < firstTwo[1].Id);
        Assert.Equal(all.Count - 2, rest.Count);
        Assert.Equal(all.Select(a => a.Id), firstTwo.Concat(rest).Select(a => a.Id));
    }

    [Fact]
    public async Task SaveAndListScenarios()
    {
        var repo = await MakeAsync();
        var mac = Mac.Compute(100m, 90m, 2000m, 1000m);
        var result = new ScenarioResult(1000m, 90m, 3, new ScenarioDelta(-1000m, -10m, 1), mac, Mac.Badge(mac, 70m), [], false, true);
        var scenario = new Scenario(Ids.NewId(), Ids.DemoWorkspace, N2, ScenarioKind.Mode, new ScenarioChange(TransportMode.Rail, null), result, Now);

        await repo.SaveScenarioAsync(Ctx, scenario, CancellationToken.None);

        var forNode = await repo.ListScenariosAsync(Ctx, N2, CancellationToken.None);
        var stored = Assert.Single(forNode);
        Assert.Equal(scenario.Id, stored.Id);
        Assert.Equal(MacBadge.WinWin, stored.Result.Badge);
        Assert.Equal(TransportMode.Rail, stored.Change.Mode);
        Assert.Empty(await repo.ListScenariosAsync(Ctx, N3, CancellationToken.None));
        Assert.Single(await repo.ListScenariosAsync(Ctx, null, CancellationToken.None));
    }

    [Fact]
    public async Task FindNodeWorksWithoutAWorkspace()
    {
        var repo = await MakeAsync();

        var node = await repo.FindNodeAsync(N2, CancellationToken.None);

        Assert.NotNull(node);
        Assert.Equal(Ids.DemoWorkspace, node!.WorkspaceId);
        Assert.Null(await repo.FindNodeAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task HealthOk()
    {
        var repo = await MakeAsync();

        Assert.True((await repo.HealthAsync(CancellationToken.None)).Ok);
        Assert.Contains(repo.Mode, new[] { "memory", "postgres" });
    }
}
