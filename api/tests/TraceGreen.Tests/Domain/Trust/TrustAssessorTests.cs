using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;
using TraceGreen.Domain.Services;
using TraceGreen.Domain.Trust;
using TraceGreen.Domain.Ubl;
using TraceGreen.Infrastructure.Memory;

namespace TraceGreen.Tests.Domain.Trust;

/// <summary>
/// The trust model over N1 (2.3 t from Shenzhen on CN-RO, whose default mode is air). Figures from <c>TestNodes</c>:
/// air transport 9 164.925 kg, sea 2.3 × 16 000 × 0.01612 = 593.216 kg, material 7 983.095622 kg (× 0.6 with renewable
/// power). N1's fictional seller id fails its check, so every N1 answer starts with 20 channel points, not 30.
/// </summary>
public class TrustAssessorTests
{
    private static readonly SeedBundle Seed = SeedLoader.Load(TestPaths.SeedDir);
    private static readonly FactorTable Table = FactorTable.FromSeed(Seed);
    private static readonly Lane CnRo = Seed.Lanes.Single(l => l.Id == "CN-RO");
    private static readonly NodeClassification ZincDiecast = new("zinc_diecast", 1.0, ClassificationSource.Invoice);
    private static readonly Invoice Invoice = N1Invoice();

    private const decimal AirGridKg = 17148.020622m;
    private const decimal SeaGridKg = 8576.311622m;

    private static Invoice N1Invoice()
    {
        var xml = File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml"));
        return NodeFactory.NewInvoice(Guid.NewGuid(), Ids.DemoWorkspace, new UblInvoiceParser().Parse(xml).Value, xml, DateTimeOffset.UnixEpoch);
    }

    private static SupplierNode N1(TransportMode? mode, EnergySource? energy, TrustInputs? trust = null, decimal? massT = null) =>
        NodeFactory.NewNode(
            Guid.NewGuid(), Invoice, ZincDiecast, CnRo, Estimate.DeriveStatus(mode, energy), mode, energy, Seed, Table,
            DateTimeOffset.UnixEpoch, massT, trust);

    private static TrustResult Assess(SupplierNode node) =>
        TrustAssessor.Assess(node, Invoice, CnRo, Seed.CategoryOf(node.CategoryId), Table)!;

    private static ClaimAssessment Claim(TrustResult result, ClaimKind kind) => result.Assessment.Claims.Single(c => c.Kind == kind);

    private static TrustInputs Container(string id = "CSQU3054383") => new(Evidence: new AnswerEvidence(ShippingDocType.ContainerNumber, id));

    [Fact]
    public void ANodeNobodyAnsweredHasNoAssessment() =>
        Assert.Null(TrustAssessor.Assess(N1(null, null), Invoice, CnRo, Seed.CategoryOf("zinc_diecast"), Table));

    [Fact]
    public void AnAnswerThatLowersNothingCountsAsClaimed()
    {
        var node = N1(TransportMode.Air, EnergySource.Grid);
        var result = Assess(node);

        Assert.Equal(AirGridKg, result.ClaimedKg);
        Assert.Equal(AirGridKg, result.CountedKg);
        Assert.Equal(AirGridKg, node.CountedKg);
        Assert.Equal(AirGridKg, node.CarbonKg);
        Assert.All(result.Assessment.Claims, c => Assert.Equal(EvidenceLevel.NotNeeded, c.Level));
        Assert.Equal(0m, result.Assessment.KgAtRisk);
        Assert.Equal(90, result.Assessment.Score); // 70 + 30 − 10 for the seller id
        Assert.Equal(TrustBand.Strong, result.Assessment.Band);
        Assert.False(result.Assessment.NeedsReview);
        Assert.Contains(result.Assessment.Signals, s => s.Code == TrustSignalCode.SellerTaxIdInvalid);
    }

    [Fact]
    public void ABareSeaAnswerOnAnAirLaneCountsAsAirUntilBacked()
    {
        var node = N1(TransportMode.Sea, EnergySource.Grid);
        var result = Assess(node);

        var mode = Claim(result, ClaimKind.Mode);
        Assert.Equal(EvidenceLevel.Unproven, mode.Level);
        Assert.True(mode.Reduces);
        Assert.Equal(8571.709m, mode.ImpactKg);
        Assert.Equal(SeaGridKg, result.ClaimedKg);
        Assert.Equal(AirGridKg, result.CountedKg);
        Assert.Equal(AirGridKg, node.CarbonKg);
        Assert.Equal(8571.709m, result.Assessment.KgAtRisk);
        Assert.Equal(38, result.Assessment.Score); // 70 × 0.25 + 20
        Assert.Equal(TrustBand.Review, result.Assessment.Band);
        Assert.True(result.Assessment.NeedsReview);
    }

    [Fact]
    public void AValidContainerNumberDocumentsTheSeaAnswer()
    {
        var result = Assess(N1(TransportMode.Sea, EnergySource.Grid, Container()));

        var mode = Claim(result, ClaimKind.Mode);
        Assert.Equal(EvidenceLevel.Documented, mode.Level);
        Assert.Contains(mode.Reasons, r => r.Code == ClaimReasonCode.DocumentGiven);
        Assert.Equal(SeaGridKg, result.CountedKg);
        Assert.Equal(0m, result.Assessment.KgAtRisk);
        Assert.Equal(69, result.Assessment.Score); // 70 × 0.7 + 20
        Assert.Equal(TrustBand.Moderate, result.Assessment.Band);
        Assert.False(result.Assessment.NeedsReview);
    }

    [Fact]
    public void AnAirWaybillForASeaAnswerContradictsIt()
    {
        var trust = new TrustInputs(Evidence: new AnswerEvidence(ShippingDocType.AirWaybill, "02012345675"));
        var result = Assess(N1(TransportMode.Sea, EnergySource.Grid, trust));

        var mode = Claim(result, ClaimKind.Mode);
        Assert.Equal(EvidenceLevel.Contradicted, mode.Level);
        Assert.Contains(mode.Reasons, r => r.Code == ClaimReasonCode.DocumentModeMismatch);
        Assert.Equal(AirGridKg, result.CountedKg);
        Assert.Equal(20, result.Assessment.Score);
        Assert.Equal(TrustBand.Review, result.Assessment.Band);
    }

    [Fact]
    public void TheCustomsDeclarationCorroboratesOrContradictsTheMode()
    {
        var agrees = Assess(N1(TransportMode.Sea, EnergySource.Grid, new TrustInputs(Records: new BuyerRecords(CustomsBorderMode: TransportMode.Sea))));
        Assert.Equal(EvidenceLevel.Corroborated, Claim(agrees, ClaimKind.Mode).Level);
        Assert.Equal(SeaGridKg, agrees.CountedKg);
        Assert.Equal(90, agrees.Assessment.Score);

        // A valid container number does not outweigh the customs record.
        var disagrees = Assess(N1(TransportMode.Sea, EnergySource.Grid,
            Container() with { Records = new BuyerRecords(CustomsBorderMode: TransportMode.Air) }));
        var mode = Claim(disagrees, ClaimKind.Mode);
        Assert.Equal(EvidenceLevel.Contradicted, mode.Level);
        Assert.Contains(mode.Reasons, r => r.Code == ClaimReasonCode.CustomsDisagrees);
        Assert.Equal(AirGridKg, disagrees.CountedKg);
        Assert.True(disagrees.Assessment.Score <= TrustPolicy.ContradictionCap);
        Assert.True(disagrees.Assessment.NeedsReview);
    }

    [Fact]
    public void AShipmentReceivedTooSoonForSeaContradictsIt()
    {
        var dispatched = new DateOnly(2026, 10, 5);
        var trust = new TrustInputs(
            Evidence: new AnswerEvidence(DispatchDate: dispatched),
            Records: new BuyerRecords(ReceivedOn: dispatched.AddDays(9)));
        var result = Assess(N1(TransportMode.Sea, EnergySource.Grid, trust));

        var mode = Claim(result, ClaimKind.Mode);
        Assert.Equal(EvidenceLevel.Contradicted, mode.Level);
        Assert.Contains(mode.Reasons, r => r.Code == ClaimReasonCode.TransitTooFast);
        Assert.Equal(AirGridKg, result.CountedKg);
    }

    [Fact]
    public void ATransitTimeThatFitsSeaAndNotAirCorroboratesSea()
    {
        var dispatched = new DateOnly(2026, 9, 1);
        var trust = new TrustInputs(
            Evidence: new AnswerEvidence(DispatchDate: dispatched),
            Records: new BuyerRecords(ReceivedOn: dispatched.AddDays(38)));
        var result = Assess(N1(TransportMode.Sea, EnergySource.Grid, trust));

        Assert.Equal(EvidenceLevel.Corroborated, Claim(result, ClaimKind.Mode).Level);
        Assert.Contains(Claim(result, ClaimKind.Mode).Reasons, r => r.Code == ClaimReasonCode.TransitFits);
        Assert.Equal(SeaGridKg, result.CountedKg);
    }

    [Fact]
    public void RenewablePowerWithoutACertificateCountsAsGrid()
    {
        var bare = Assess(N1(TransportMode.Air, EnergySource.Renewable));
        var energy = Claim(bare, ClaimKind.Energy);
        Assert.Equal(EvidenceLevel.Unproven, energy.Level);
        Assert.Contains(energy.Reasons, r => r.Code == ClaimReasonCode.NoCertificate);
        Assert.Equal(3193.2382488m, energy.ImpactKg); // 40% of the material figure
        Assert.Equal(AirGridKg, bare.CountedKg);

        var certified = Assess(N1(TransportMode.Air, EnergySource.Renewable,
            new TrustInputs(Evidence: new AnswerEvidence(EnergyCertType: EnergyCertType.IRec, EnergyCertId: "IREC-CN-2026-0042"))));
        Assert.Equal(EvidenceLevel.Documented, Claim(certified, ClaimKind.Energy).Level);
        Assert.Equal(AirGridKg - 3193.2382488m, certified.CountedKg);
    }

    [Fact]
    public void APersonsDecisionOutranksTheEvidence()
    {
        var accepted = Assess(N1(TransportMode.Sea, EnergySource.Grid, new TrustInputs(ModeReview: ClaimDecision.Accepted)));
        Assert.Equal(EvidenceLevel.Reviewed, Claim(accepted, ClaimKind.Mode).Level);
        Assert.Equal(SeaGridKg, accepted.CountedKg);
        Assert.Equal(90, accepted.Assessment.Score);

        var rejected = Assess(N1(TransportMode.Sea, EnergySource.Grid, Container() with { ModeReview = ClaimDecision.Rejected }));
        Assert.Equal(EvidenceLevel.Contradicted, Claim(rejected, ClaimKind.Mode).Level);
        Assert.Equal(ClaimReasonCode.RejectedByBuyer, Claim(rejected, ClaimKind.Mode).Reasons[0].Code);
        Assert.Equal(AirGridKg, rejected.CountedKg);
    }

    [Fact]
    public void AShippingMassBelowTheInvoicesGoodsIsContradicted()
    {
        var result = Assess(N1(TransportMode.Air, EnergySource.Grid, massT: 2.0m));

        var mass = Claim(result, ClaimKind.Mass);
        Assert.Equal("2.0", mass.Value);
        Assert.Equal(EvidenceLevel.Contradicted, mass.Level);
        Assert.Contains(mass.Reasons, r => r.Code == ClaimReasonCode.MassBelowInvoice);
        Assert.Equal(2.0m * (7500m * 0.5313m + 3470.91114m), result.ClaimedKg);
        Assert.Equal(AirGridKg, result.CountedKg);

        // A higher mass raises the figure, so it is taken as given.
        var heavier = Assess(N1(TransportMode.Air, EnergySource.Grid, massT: 3m));
        Assert.Equal(EvidenceLevel.NotNeeded, Claim(heavier, ClaimKind.Mass).Level);
        Assert.Equal(heavier.ClaimedKg, heavier.CountedKg);
    }

    [Fact]
    public void AShippingMassWithNoInvoiceMassToCompareNeedsBackingToo()
    {
        // The invoice counts pieces, not kilograms, so only the supplier's mass gives an activity figure.
        var xml = File.ReadAllText(TestPaths.Invoice("N1-shenzhen.xml")).Replace("unitCode=\"KGM\"", "unitCode=\"H87\"");
        var invoice = NodeFactory.NewInvoice(Guid.NewGuid(), Ids.DemoWorkspace, new UblInvoiceParser().Parse(xml).Value, xml, DateTimeOffset.UnixEpoch);
        var node = NodeFactory.NewNode(
            Guid.NewGuid(), invoice, ZincDiecast, CnRo, NodeStatus.Verified, TransportMode.Air, EnergySource.Grid, Seed, Table,
            DateTimeOffset.UnixEpoch, confirmedMassT: 0.001m);
        var result = TrustAssessor.Assess(node, invoice, CnRo, Seed.CategoryOf("zinc_diecast"), Table)!;

        var mass = Claim(result, ClaimKind.Mass);
        Assert.Equal(EvidenceLevel.Unproven, mass.Level);
        Assert.True(mass.Reduces);
        // Until something backs it, no activity figure counts: the spend estimate stands.
        Assert.Equal(node.SpendKg, result.CountedKg);
        Assert.Equal(node.SpendKg, node.CarbonKg);
        Assert.Null(result.CountedState.MassT);
        Assert.Equal(node.SpendKg - result.ClaimedKg, result.Assessment.KgAtRisk);
        Assert.True(result.Assessment.NeedsReview);
    }

    [Fact]
    public void HowTheAnswerArrivedTakesPointsOffTheChannelPart()
    {
        var context = new AnswerContext(SameNetworkAsLinkMaker: true, LinkAgeSeconds: 30, ModeChangedOnLane: false, PriorRejection: false, Simulated: false);
        var result = Assess(N1(TransportMode.Air, EnergySource.Grid, new TrustInputs(Answer: context)));

        Assert.Equal(
            [TrustSignalCode.SameNetworkAsLinkMaker, TrustSignalCode.FastAnswer, TrustSignalCode.SellerTaxIdInvalid],
            result.Assessment.Signals.Select(s => s.Code));
        Assert.Equal(70, result.Assessment.Score); // 70 + max(0, 30 − 20 − 10 − 10)
        Assert.Equal(TrustBand.Moderate, result.Assessment.Band);

        var simulated = Assess(N1(TransportMode.Air, EnergySource.Grid, new TrustInputs(Answer: context with { SameNetworkAsLinkMaker = null, LinkAgeSeconds = 600, Simulated = true })));
        Assert.Equal(TrustSignalCode.SimulatedAnswer, simulated.Assessment.Signals[0].Code);
        Assert.Equal(70, simulated.Assessment.Score);
    }

    [Fact]
    public void APartialNodeStaysOnItsEstimateWithNothingAtRisk()
    {
        var node = N1(TransportMode.Sea, null);
        var result = Assess(node);

        Assert.Equal(NodeStatus.Partial, node.Status);
        Assert.Null(result.CountedKg);
        Assert.Equal(0m, result.Assessment.KgAtRisk);
        Assert.Equal(node.SpendKg, node.CarbonKg);
        // Its unbacked sea answer changes no figure yet, so it waits for nobody; a contradiction still does.
        Assert.Equal(EvidenceLevel.Unproven, Claim(result, ClaimKind.Mode).Level);
        Assert.False(result.Assessment.NeedsReview);
        var contradicted = Assess(N1(TransportMode.Sea, null, new TrustInputs(Records: new BuyerRecords(CustomsBorderMode: TransportMode.Air))));
        Assert.True(contradicted.Assessment.NeedsReview);
    }

    [Fact]
    public async Task SeedNodesKeepTheirFigures()
    {
        // No seeded answer claims a reduction against its lane's default (N2 sea on TW-RO, N5 road on DE), and the demo
        // answer for N1 is air + grid on an air lane, so the hotspot ring, the badges and the KPIs stay as they were.
        var repo = new MemoryRepository();
        await repo.SeedAsync(Seed, reset: true, TestContext.Current.CancellationToken);
        var ctx = new Ctx(Ids.DemoWorkspace, "test");
        foreach (var node in await repo.ListNodesAsync(ctx, TestContext.Current.CancellationToken))
        {
            var invoice = (await repo.GetInvoiceAsync(ctx, node.InvoiceId, TestContext.Current.CancellationToken))!;
            var lane = Seed.Lanes.Single(l => l.Id == node.LaneId);
            var result = TrustAssessor.Assess(node, invoice, lane, Seed.CategoryOf(node.CategoryId), Table);
            Assert.Equal(0m, result?.Assessment.KgAtRisk ?? 0m);
            if (node.Status == NodeStatus.Verified)
            {
                Assert.Equal(node.ActivityKg, node.CountedKg);
                Assert.Equal(node.ActivityKg, node.CarbonKg);
            }
        }
        var demo = N1(TransportMode.Air, EnergySource.Grid);
        Assert.Equal(demo.ActivityKg, demo.CarbonKg);
    }
}
