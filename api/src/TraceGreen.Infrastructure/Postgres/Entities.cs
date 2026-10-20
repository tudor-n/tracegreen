namespace TraceGreen.Infrastructure.Postgres;

// EF Core rows. Enums are stored by name, nested documents as jsonb text; mapping lives in Mapping.cs.

public class InvoiceRow
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string VendorName { get; set; } = "";
    public string VendorCui { get; set; } = "";
    public string BuyerCui { get; set; } = "";
    public DateOnly IssueDate { get; set; }
    public string Currency { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string RawXmlHash { get; set; } = "";
    public string InvoiceHash { get; set; } = "";
    public string ExtractedJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public class NodeRow
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InvoiceId { get; set; }
    public string Label { get; set; } = "";
    public string Country { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public string SectorId { get; set; } = "";
    public double? AiConfidence { get; set; }
    public string ClassificationSource { get; set; } = "";
    public decimal? MassT { get; set; }
    public string LaneId { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal SpendKg { get; set; }
    public decimal? ActivityKg { get; set; }
    public decimal SpendEur { get; set; }
    public decimal CostEur { get; set; }
    public int LeadDays { get; set; }
    public string? TransportMode { get; set; }
    public string? EnergySource { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class VerificationRow
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid NodeId { get; set; }
    public string TokenNonce { get; set; } = "";
    public string? TransportMode { get; set; }
    public string? EnergySource { get; set; }
    public decimal? MassTConfirmed { get; set; }
    public DateTimeOffset ConsumedAt { get; set; }
    public string IpHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class AuditRow
{
    public long Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public DateTimeOffset Ts { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string PrevHash { get; set; } = "";
    public string Hash { get; set; } = "";
}

public class ScenarioRow
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid NodeId { get; set; }
    public string Kind { get; set; } = "";
    public string ChangeJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public class FactorRow
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public decimal Value { get; set; }
    public string Unit { get; set; } = "";
    public string Source { get; set; } = "";
    public int Year { get; set; }
    public string RowLabel { get; set; } = "";
}

public class LaneRow
{
    public string Id { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Dest { get; set; } = "";
    public string ModeDefault { get; set; } = "";
    public string DistanceKmJson { get; set; } = "{}";
    public string LeadDaysJson { get; set; } = "{}";
    public string CostPerKgJson { get; set; } = "{}";
    public string Note { get; set; } = "";
}

public class AlternativeRow
{
    public string Id { get; set; } = "";
    public string NodeCategory { get; set; } = "";
    public string Label { get; set; } = "";
    public string Country { get; set; } = "";
    public string LaneId { get; set; } = "";
    public decimal CostEur { get; set; }
    public decimal MassT { get; set; }
}

public class BuyerRow
{
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Cui { get; set; } = "";
    public string Country { get; set; } = "";
    public string City { get; set; } = "";
}
