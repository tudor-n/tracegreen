using System.Text.Json.Nodes;
using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Security;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Trust;

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
        Ids.NewId(), Ids.DemoWorkspace, nodeId, nonce, TransportMode.Road, EnergySource.Grid, 1.4m, Now, "iphash", Now,
        "2026-11-06", "terms-sha256");

    private static readonly NodePatch VerifiedPatch = new(NodeStatus.Verified, 123m, TransportMode.Road, EnergySource.Grid, 1.4m, 4321m, 3);

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
        Assert.Equal("RO99999992", (await repo.GetBuyerAsync(Ctx, CancellationToken.None)).Cui);
        Assert.Single(await repo.ListVerificationsAsync(Ctx, N2, CancellationToken.None));
        Assert.All(nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.ClassificationRationale)));
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
        var invoice = new Invoice(invoiceId, Ids.DemoWorkspace, "Vendor", "RO1", "RO99999992", new DateOnly(2026, 10, 1), "EUR", 100m,
            "raw" + invoiceId.ToString("N"), "inv" + invoiceId.ToString("N"),
            new ExtractedInvoice("X-1", new DateOnly(2026, 10, 1), "EUR", 100m, new Party("Vendor", "RO1", "RO"), new Party("Buyer", "RO99999992", "RO"),
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

    [Fact]
    public async Task TheClassificationReasonRoundTripsAndSurvivesAVerification()
    {
        var repo = await MakeAsync();
        var (invoice, node) = NewImport("raw-reason" + Ids.NewId().ToString("N"));
        node = node with { ClassificationRationale = "Zinc die-cast casing, sensor alloy" };

        Assert.Equal(InsertInvoiceStatus.Ok, (await repo.InsertInvoiceWithNodeAsync(Ctx, invoice, node, Audit("invoice.imported"), CancellationToken.None)).Status);
        Assert.Equal("Zinc die-cast casing, sensor alloy", (await repo.GetNodeAsync(Ctx, node.Id, CancellationToken.None))!.ClassificationRationale);

        var recorded = await repo.RecordVerificationAsync(
            Ctx, NewVerification(node.Id, "nonce-reason" + Ids.NewId().ToString("N")), VerifiedPatch, Audit("verification.submitted"), null, CancellationToken.None);
        Assert.Equal(RecordVerificationStatus.Ok, recorded.Status);
        Assert.Equal("Zinc die-cast casing, sensor alloy", (await repo.GetNodeAsync(Ctx, node.Id, CancellationToken.None))!.ClassificationRationale);
    }

    [Fact]
    public async Task TheDocumentKindRoundTripsAndDefaultsToUbl()
    {
        var repo = await MakeAsync();
        var (invoice, node) = NewImport("raw-pdf" + Ids.NewId().ToString("N"));

        await repo.InsertInvoiceWithNodeAsync(Ctx, invoice with { DocumentKind = "pdf" }, node, Audit("invoice.imported"), CancellationToken.None);

        Assert.Equal("pdf", (await repo.GetInvoiceAsync(Ctx, invoice.Id, CancellationToken.None))!.DocumentKind);
        var seeded = (await repo.ListNodesAsync(Ctx, CancellationToken.None)).First(n => n.Id == N2);
        Assert.Equal("ubl", (await repo.GetInvoiceAsync(Ctx, seeded.InvoiceId, CancellationToken.None))!.DocumentKind);
    }

    private static (Invoice Invoice, SupplierNode Node) NewImport(string rawHash)
    {
        var invoiceId = Ids.NewId();
        var invoice = new Invoice(invoiceId, Ids.DemoWorkspace, "Vendor", "RO1", "RO99999992", new DateOnly(2026, 10, 1), "EUR", 100m,
            rawHash, "inv" + invoiceId.ToString("N"),
            new ExtractedInvoice("X-1", new DateOnly(2026, 10, 1), "EUR", 100m, new Party("Vendor", "RO1", "RO"), new Party("Buyer", "RO99999992", "RO"),
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

        var result = await repo.RecordVerificationAsync(Ctx, NewVerification(N3, "fresh-nonce-1"), VerifiedPatch, Audit("verification.submitted"), null, CancellationToken.None);

        Assert.Equal(RecordVerificationStatus.Ok, result.Status);
        Assert.Equal(NodeStatus.Verified, result.Node!.Status);
        Assert.Equal(123m, result.Node.ActivityKg);
        var stored = await repo.GetNodeAsync(Ctx, N3, CancellationToken.None);
        Assert.Equal(NodeStatus.Verified, stored!.Status);
        Assert.Equal(TransportMode.Road, stored.TransportMode);
        Assert.Equal(4321m, stored.CostEur);
        Assert.Equal(3, stored.LeadDays);
        var verification = Assert.Single(await repo.ListVerificationsAsync(Ctx, N3, CancellationToken.None));
        Assert.Equal("2026-11-06", verification.TermsVersion);
        Assert.Equal("terms-sha256", verification.TermsSha256);
        var audit = await AllAudit(repo);
        Assert.Equal(auditBefore + 1, audit.Count);
        Assert.True(AuditChain.Verify(audit).Valid);
    }

    [Fact]
    public async Task StoredExtractHashesToTheStoredInvoiceHash()
    {
        // The verify flow recomputes the hash from the stored extract (C2), so a storage round trip must not change it.
        var repo = await MakeAsync();

        foreach (var node in await repo.ListNodesAsync(Ctx, CancellationToken.None))
        {
            var invoice = await repo.GetInvoiceAsync(Ctx, node.InvoiceId, CancellationToken.None);
            Assert.Equal(invoice!.InvoiceHash, Hashing.InvoiceHash(invoice.Extracted));
        }
    }

    [Fact]
    public async Task RecordVerificationWithUsedNonceReturnsAlreadyUsedAndChangesNothing()
    {
        var repo = await MakeAsync();
        var auditBefore = (await AllAudit(repo)).Count;
        var usedNonce = Bundle.DemoState.Nodes.Single(n => n.Key == "N2").Nonce!;

        var result = await repo.RecordVerificationAsync(Ctx, NewVerification(N3, usedNonce), VerifiedPatch, Audit("verification.submitted"), null, CancellationToken.None);

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
            Task.Run(() => repo.RecordVerificationAsync(Ctx, NewVerification(N3, "race-nonce"), VerifiedPatch, Audit("verification.submitted"), null, CancellationToken.None))));

        Assert.Equal(1, results.Count(r => r.Status == RecordVerificationStatus.Ok));
        Assert.Equal(9, results.Count(r => r.Status == RecordVerificationStatus.AlreadyUsed));
        Assert.True(AuditChain.Verify(await AllAudit(repo)).Valid);
    }

    private static readonly NodeAnswerState Unanswered = new(NodeStatus.Estimated, null, null);

    [Fact]
    public async Task RecordVerificationWritesWhenTheNodeIsAsExpected()
    {
        var repo = await MakeAsync();

        var result = await repo.RecordVerificationAsync(
            Ctx, NewVerification(N3, "expected-ok"), VerifiedPatch, Audit("verification.submitted"), Unanswered, CancellationToken.None);

        Assert.Equal(RecordVerificationStatus.Ok, result.Status);
        Assert.Equal(NodeStatus.Verified, (await repo.GetNodeAsync(Ctx, N3, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task RecordVerificationOnAChangedNodeWritesNothingAndLeavesItsNonceFree()
    {
        var repo = await MakeAsync();
        var first = await repo.RecordVerificationAsync(
            Ctx, NewVerification(N3, "first"), VerifiedPatch, Audit("verification.submitted"), Unanswered, CancellationToken.None);
        Assert.Equal(RecordVerificationStatus.Ok, first.Status);
        var auditBefore = (await AllAudit(repo)).Count;
        var otherPatch = VerifiedPatch with { EnergySource = EnergySource.Renewable, ActivityKg = 99m };

        // Built on the node as it was before the first answer.
        var stale = await repo.RecordVerificationAsync(
            Ctx, NewVerification(N3, "stale"), otherPatch, Audit("verification.submitted"), Unanswered, CancellationToken.None);

        Assert.Equal(RecordVerificationStatus.NodeChanged, stale.Status);
        Assert.Null(stale.Entry);
        var stored = (await repo.GetNodeAsync(Ctx, N3, CancellationToken.None))!;
        Assert.Equal(EnergySource.Grid, stored.EnergySource);
        Assert.Equal(123m, stored.ActivityKg);
        Assert.Single(await repo.ListVerificationsAsync(Ctx, N3, CancellationToken.None));
        Assert.Equal(auditBefore, (await AllAudit(repo)).Count);
        // The refused answer did not use up its nonce.
        var retried = await repo.RecordVerificationAsync(
            Ctx, NewVerification(N3, "stale"), otherPatch, Audit("verification.submitted"), NodeAnswerState.Of(stored), CancellationToken.None);
        Assert.Equal(RecordVerificationStatus.Ok, retried.Status);
    }

    [Fact]
    public async Task TenConcurrentAnswersBuiltOnTheSameNodeExactlyOneLands()
    {
        var repo = await MakeAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => repo.RecordVerificationAsync(
                Ctx, NewVerification(N3, $"same-state-{i}"), VerifiedPatch, Audit("verification.submitted"), Unanswered, CancellationToken.None))));

        Assert.Equal(1, results.Count(r => r.Status == RecordVerificationStatus.Ok));
        Assert.Equal(9, results.Count(r => r.Status == RecordVerificationStatus.NodeChanged));
        Assert.Single(await repo.ListVerificationsAsync(Ctx, N3, CancellationToken.None));
        Assert.True(AuditChain.Verify(await AllAudit(repo)).Valid);
    }

    /// <summary>A reviewer that moves the node to <paramref name="categoryId"/> with fixed figures; the contract tests storage, not math.</summary>
    private static ClassificationReviewer Reviewer(string categoryId, DateTimeOffset? at = null, string lineKey = "line-key") => current => new ClassificationWrite(
        new ClassificationPatch(categoryId, "plastics_other", ClassificationSource.Buyer, null, ClassificationReview.Corrected,
            at ?? Now, 777.5m, current.ActivityKg is null ? null : 888.25m),
        lineKey,
        Audit("classification.reviewed", new JsonObject { ["fromCategoryId"] = current.CategoryId }));

    [Fact]
    public async Task AReviewRemembersItsCategoryForTheLineKeyUntilAReset()
    {
        var repo = await MakeAsync();
        var key = "line-" + Ids.NewId().ToString("N");
        Assert.Empty(await repo.ListBuyerClassificationsAsync(Ctx, [key], CancellationToken.None));

        await repo.ReviewClassificationAsync(Ctx, N2, "pcb", Reviewer("plastics", lineKey: key), CancellationToken.None);
        var first = await repo.ListBuyerClassificationsAsync(Ctx, [key, "other-key", key], CancellationToken.None);
        await repo.ReviewClassificationAsync(Ctx, N2, "plastics", Reviewer("connectors", lineKey: key), CancellationToken.None);
        var second = await repo.ListBuyerClassificationsAsync(Ctx, [key], CancellationToken.None);
        await repo.SeedAsync(Bundle, reset: true, CancellationToken.None);

        Assert.Equal("plastics", Assert.Single(first, kv => kv.Key == key).Value);
        Assert.Single(first);
        Assert.Equal("connectors", second[key]);
        Assert.Empty(await repo.ListBuyerClassificationsAsync(Ctx, [key], CancellationToken.None));
        Assert.Empty(await repo.ListBuyerClassificationsAsync(Ctx, [], CancellationToken.None));
    }

    [Fact]
    public async Task AStaleReviewRemembersNothing()
    {
        var repo = await MakeAsync();
        var key = "line-" + Ids.NewId().ToString("N");

        await repo.ReviewClassificationAsync(Ctx, N3, "pcb", Reviewer("plastics", lineKey: key), CancellationToken.None);

        Assert.Empty(await repo.ListBuyerClassificationsAsync(Ctx, [key], CancellationToken.None));
    }

    [Fact]
    public async Task SeededNodesCarryTheirReviewState()
    {
        var repo = await MakeAsync();

        var n3 = (await repo.GetNodeAsync(Ctx, N3, CancellationToken.None))!;
        var n2 = (await repo.GetNodeAsync(Ctx, N2, CancellationToken.None))!;

        // N3's category comes from the AI cache, N2's from the invoice's CPV code.
        Assert.Equal(ClassificationReview.Pending, n3.Review);
        Assert.Null(n3.ReviewedAt);
        Assert.Equal(ClassificationReview.NotNeeded, n2.Review);
    }

    [Fact]
    public async Task ReviewClassificationWritesThePatchAndAppendsAudit()
    {
        var repo = await MakeAsync();
        var before = await AllAudit(repo);
        var at = new DateTimeOffset(2026, 11, 6, 12, 30, 15, 123, TimeSpan.Zero);

        var result = await repo.ReviewClassificationAsync(Ctx, N2, "pcb", Reviewer("plastics", at), CancellationToken.None);

        Assert.Equal(ReviewClassificationStatus.Ok, result.Status);
        var stored = (await repo.GetNodeAsync(Ctx, N2, CancellationToken.None))!;
        Assert.Equal("plastics", stored.CategoryId);
        Assert.Equal("plastics_other", stored.SectorId);
        Assert.Equal(ClassificationSource.Buyer, stored.ClassificationSource);
        Assert.Null(stored.AiConfidence);
        Assert.Equal(ClassificationReview.Corrected, stored.Review);
        Assert.Equal(at, stored.ReviewedAt);
        Assert.Equal(777.5m, stored.SpendKg);
        Assert.Equal(888.25m, stored.ActivityKg);
        // Status, answers, cost and lead time are not the review's to change.
        Assert.Equal(NodeStatus.Verified, stored.Status);
        Assert.Equal(TransportMode.Sea, stored.TransportMode);
        Assert.Equal(stored, result.Node);
        var after = await AllAudit(repo);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal("classification.reviewed", result.Entry!.Action);
        Assert.Equal("pcb", result.Entry.Payload["fromCategoryId"]!.GetValue<string>());
        Assert.True(AuditChain.Verify(after).Valid);
    }

    [Fact]
    public async Task ReviewClassificationWithAStaleCategoryWritesNothing()
    {
        var repo = await MakeAsync();
        var before = await repo.GetNodeAsync(Ctx, N3, CancellationToken.None);
        var auditBefore = (await AllAudit(repo)).Count;
        var called = false;

        var result = await repo.ReviewClassificationAsync(Ctx, N3, "pcb", current =>
        {
            called = true;
            return Reviewer("plastics")(current);
        }, CancellationToken.None);

        Assert.Equal(ReviewClassificationStatus.CategoryChanged, result.Status);
        Assert.Equal("wire_harness", result.Node!.CategoryId);
        Assert.Null(result.Entry);
        Assert.False(called);
        Assert.Equal(before, await repo.GetNodeAsync(Ctx, N3, CancellationToken.None));
        Assert.Equal(auditBefore, (await AllAudit(repo)).Count);
    }

    [Fact]
    public async Task ReviewClassificationOfAnUnknownNodeIsNotFound()
    {
        var repo = await MakeAsync();

        var result = await repo.ReviewClassificationAsync(Ctx, N1, "zinc_diecast", Reviewer("plastics"), CancellationToken.None);

        Assert.Equal(ReviewClassificationStatus.NodeNotFound, result.Status);
        Assert.Null(result.Node);
    }

    [Fact]
    public async Task TenConcurrentReviewsBuiltOnTheSameCategoryExactlyOneLands()
    {
        var repo = await MakeAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => repo.ReviewClassificationAsync(Ctx, N3, "wire_harness", Reviewer("plastics"), CancellationToken.None))));

        Assert.Equal(1, results.Count(r => r.Status == ReviewClassificationStatus.Ok));
        Assert.Equal(9, results.Count(r => r.Status == ReviewClassificationStatus.CategoryChanged));
        Assert.True(AuditChain.Verify(await AllAudit(repo)).Valid);
    }

    [Fact]
    public async Task ReviewSeesAVerificationThatLandedBeforeIt()
    {
        var repo = await MakeAsync();
        await repo.RecordVerificationAsync(Ctx, NewVerification(N3, "before-review"), VerifiedPatch, Audit("verification.submitted"), null, CancellationToken.None);
        SupplierNode? seen = null;

        await repo.ReviewClassificationAsync(Ctx, N3, "wire_harness", current =>
        {
            seen = current;
            return Reviewer("plastics")(current);
        }, CancellationToken.None);

        Assert.Equal(NodeStatus.Verified, seen!.Status);
        Assert.Equal(123m, seen.ActivityKg);
        Assert.Equal(888.25m, (await repo.GetNodeAsync(Ctx, N3, CancellationToken.None))!.ActivityKg);
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

    private static readonly TrustInputs SomeTrust = new(
        Evidence: new AnswerEvidence(ShippingDocType.ContainerNumber, "CSQU3054383", new DateOnly(2026, 10, 1), EnergyCertType.IRec, "IREC-CN-2026-0042"),
        Answer: new AnswerContext(SameNetworkAsLinkMaker: true, LinkAgeSeconds: 42, ModeChangedOnLane: false, PriorRejection: true, Simulated: false),
        Records: new BuyerRecords(new DateOnly(2026, 11, 2), TransportMode.Sea, "26RO1234567890ABCD"),
        ModeReview: ClaimDecision.Accepted,
        EnergyReview: ClaimDecision.Rejected);

    [Fact]
    public async Task ATrustWriteStoresItsInputsCountedFigureAndTargetWithItsAuditEntry()
    {
        var repo = await MakeAsync();
        var target = new TargetChoice(TransportMode.Sea, EnergySource.Renewable, null);
        SupplierNode? seen = null;

        var result = await repo.UpdateTrustAsync(Ctx, N2, current =>
        {
            seen = current;
            return new TrustWrite(SomeTrust, 777.5m, target, Audit("trust.test"));
        }, CancellationToken.None);

        Assert.Equal(UpdateTrustStatus.Ok, result.Status);
        Assert.Equal(N2, seen!.Id);
        Assert.Equal("trust.test", result.Entry!.Action);
        var stored = (await repo.GetNodeAsync(Ctx, N2, CancellationToken.None))!;
        Assert.Equal(SomeTrust, stored.Trust);
        Assert.Equal(777.5m, stored.CountedKg);
        Assert.Equal(target, stored.Target);
        Assert.Equal(777.5m, stored.CarbonKg); // N2 is verified, so it counts the counted figure
        var audit = await AllAudit(repo);
        Assert.Equal("trust.test", audit[^1].Action);
        Assert.True(AuditChain.Verify(audit).Valid);
    }

    [Fact]
    public async Task ARefusedTrustWriteWritesNothing()
    {
        var repo = await MakeAsync();
        var before = (await repo.GetNodeAsync(Ctx, N2, CancellationToken.None))!;
        var auditBefore = (await AllAudit(repo)).Count;

        var result = await repo.UpdateTrustAsync(Ctx, N2, _ => null, CancellationToken.None);

        Assert.Equal(UpdateTrustStatus.Refused, result.Status);
        Assert.Null(result.Entry);
        Assert.Equal(before, await repo.GetNodeAsync(Ctx, N2, CancellationToken.None));
        Assert.Equal(auditBefore, (await AllAudit(repo)).Count);
        Assert.Equal(UpdateTrustStatus.NodeNotFound, (await repo.UpdateTrustAsync(Ctx, Guid.NewGuid(), _ => null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task AVerificationKeepsItsEvidenceAndItsPatchTheTrustInputs()
    {
        var repo = await MakeAsync();
        var verification = NewVerification(N3, "nonce-evidence") with { Evidence = SomeTrust.Evidence };
        var patch = VerifiedPatch with { Trust = SomeTrust, CountedKg = 150m };

        var result = await repo.RecordVerificationAsync(Ctx, verification, patch, Audit("verification.submitted"), null, CancellationToken.None);

        Assert.Equal(RecordVerificationStatus.Ok, result.Status);
        Assert.Equal(SomeTrust, result.Node!.Trust);
        Assert.Equal(150m, result.Node.CountedKg);
        var stored = (await repo.GetNodeAsync(Ctx, N3, CancellationToken.None))!;
        Assert.Equal(SomeTrust, stored.Trust);
        Assert.Equal(150m, stored.CarbonKg);
        var kept = Assert.Single(await repo.ListVerificationsAsync(Ctx, N3, CancellationToken.None));
        Assert.Equal(SomeTrust.Evidence, kept.Evidence);
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
