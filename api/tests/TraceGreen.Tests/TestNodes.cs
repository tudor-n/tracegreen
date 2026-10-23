using TraceGreen.Domain.Model;

namespace TraceGreen.Tests;

/// <summary>Hand-computed fixtures matching the seed scenario (placeholder factors).</summary>
public static class TestNodes
{
    public static readonly Guid N1Id = Guid.Parse("11111111-1111-4111-8111-000000000001");
    public static readonly Guid N1InvoiceId = Guid.Parse("22222222-2222-4222-8222-000000000001");

    /// <summary>N1 after verification (air, grid): transport 2.3 t × 7500 km × 0.80 = 13 800 kg, material 2.3 × 3000 = 6 900 kg.</summary>
    public static SupplierNode N1(
        NodeStatus status = NodeStatus.Verified,
        TransportMode? mode = TransportMode.Air,
        EnergySource? energy = EnergySource.Grid) => new(
        Id: N1Id,
        WorkspaceId: Ids.DemoWorkspace,
        InvoiceId: N1InvoiceId,
        Label: "Shenzhen Diecast Co.",
        Country: "CN",
        CategoryId: "zinc_diecast",
        SectorId: "diecast_nonferrous",
        AiConfidence: 1.0,
        ClassificationSource: ClassificationSource.Invoice,
        MassT: 2.3m,
        LaneId: "CN-RO",
        Status: status,
        SpendKg: 21600m,
        ActivityKg: status == NodeStatus.Estimated ? null : 20700m,
        SpendEur: 48000m,
        CostEur: 52600m,
        LeadDays: 7,
        TransportMode: mode,
        EnergySource: energy,
        CreatedAt: DateTimeOffset.UnixEpoch);

    public static SupplierNode Simple(string label, NodeStatus status, decimal spendKg, decimal? activityKg, decimal spendEur = 1000m) => new(
        Id: Guid.NewGuid(),
        WorkspaceId: Ids.DemoWorkspace,
        InvoiceId: Guid.NewGuid(),
        Label: label,
        Country: "RO",
        CategoryId: "plastics",
        SectorId: "misc_manufacturing",
        AiConfidence: null,
        ClassificationSource: ClassificationSource.Stub,
        MassT: 1m,
        LaneId: "RO-SB-SB",
        Status: status,
        SpendKg: spendKg,
        ActivityKg: activityKg,
        SpendEur: spendEur,
        CostEur: spendEur,
        LeadDays: 1,
        TransportMode: status == NodeStatus.Estimated ? null : TransportMode.Road,
        EnergySource: status == NodeStatus.Verified ? EnergySource.Grid : null,
        CreatedAt: DateTimeOffset.UnixEpoch);
}
