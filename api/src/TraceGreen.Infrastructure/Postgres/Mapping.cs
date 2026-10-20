using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TraceGreen.Domain.Carbon;
using TraceGreen.Domain.Model;

namespace TraceGreen.Infrastructure.Postgres;

internal static class Mapping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static string Ser<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T De<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)
        ?? throw new InvalidOperationException($"Stored JSON for {typeof(T).Name} is null.");

    private static string Name<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString();

    private static string? Name<TEnum>(TEnum? value) where TEnum : struct, Enum => value?.ToString();

    private static TEnum Parse<TEnum>(string value) where TEnum : struct, Enum => Enum.Parse<TEnum>(value, ignoreCase: true);

    private static TEnum? ParseOpt<TEnum>(string? value) where TEnum : struct, Enum =>
        value is null ? null : Enum.Parse<TEnum>(value, ignoreCase: true);

    private static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static InvoiceRow ToRow(Invoice i) => new()
    {
        Id = i.Id, WorkspaceId = i.WorkspaceId, VendorName = i.VendorName, VendorCui = i.VendorCui, BuyerCui = i.BuyerCui,
        IssueDate = i.IssueDate, Currency = i.Currency, TotalAmount = i.TotalAmount, RawXmlHash = i.RawXmlHash,
        InvoiceHash = i.InvoiceHash, ExtractedJson = Ser(i.Extracted), CreatedAt = Utc(i.CreatedAt),
    };

    public static Invoice ToDomain(InvoiceRow r) => new(
        r.Id, r.WorkspaceId, r.VendorName, r.VendorCui, r.BuyerCui, r.IssueDate, r.Currency, r.TotalAmount,
        r.RawXmlHash, r.InvoiceHash, De<ExtractedInvoice>(r.ExtractedJson), r.CreatedAt);

    public static NodeRow ToRow(SupplierNode n) => new()
    {
        Id = n.Id, WorkspaceId = n.WorkspaceId, InvoiceId = n.InvoiceId, Label = n.Label, Country = n.Country,
        CategoryId = n.CategoryId, SectorId = n.SectorId, AiConfidence = n.AiConfidence,
        ClassificationSource = Name(n.ClassificationSource), MassT = n.MassT, LaneId = n.LaneId, Status = Name(n.Status),
        SpendKg = n.SpendKg, ActivityKg = n.ActivityKg, SpendEur = n.SpendEur, CostEur = n.CostEur, LeadDays = n.LeadDays,
        TransportMode = Name(n.TransportMode), EnergySource = Name(n.EnergySource), CreatedAt = Utc(n.CreatedAt),
    };

    public static void Apply(NodeRow row, NodePatch patch)
    {
        row.Status = Name(patch.Status);
        row.ActivityKg = patch.ActivityKg;
        row.TransportMode = Name(patch.TransportMode);
        row.EnergySource = Name(patch.EnergySource);
        row.MassT = patch.MassT ?? row.MassT;
    }

    public static SupplierNode ToDomain(NodeRow r) => new(
        r.Id, r.WorkspaceId, r.InvoiceId, r.Label, r.Country, r.CategoryId, r.SectorId, r.AiConfidence,
        Parse<ClassificationSource>(r.ClassificationSource), r.MassT, r.LaneId, Parse<NodeStatus>(r.Status),
        r.SpendKg, r.ActivityKg, r.SpendEur, r.CostEur, r.LeadDays,
        ParseOpt<TransportMode>(r.TransportMode), ParseOpt<EnergySource>(r.EnergySource), r.CreatedAt);

    public static VerificationRow ToRow(Verification v) => new()
    {
        Id = v.Id, WorkspaceId = v.WorkspaceId, NodeId = v.NodeId, TokenNonce = v.TokenNonce,
        TransportMode = Name(v.TransportMode), EnergySource = Name(v.EnergySource), MassTConfirmed = v.MassTConfirmed,
        ConsumedAt = Utc(v.ConsumedAt), IpHash = v.IpHash, CreatedAt = Utc(v.CreatedAt),
    };

    public static Verification ToDomain(VerificationRow r) => new(
        r.Id, r.WorkspaceId, r.NodeId, r.TokenNonce, ParseOpt<TransportMode>(r.TransportMode),
        ParseOpt<EnergySource>(r.EnergySource), r.MassTConfirmed, r.ConsumedAt, r.IpHash, r.CreatedAt);

    public static AuditRow ToRow(NewAuditEntry e) => new()
    {
        WorkspaceId = e.WorkspaceId, Ts = Utc(e.Ts), Actor = e.Actor, Action = e.Action,
        PayloadJson = e.Payload.ToJsonString(), PrevHash = e.PrevHash, Hash = e.Hash,
    };

    public static AuditEntry ToDomain(AuditRow r) => new(
        r.Id, r.WorkspaceId, r.Ts, r.Actor, r.Action, JsonNode.Parse(r.PayloadJson) ?? new JsonObject(), r.PrevHash, r.Hash);

    public static ScenarioRow ToRow(Scenario s) => new()
    {
        Id = s.Id, WorkspaceId = s.WorkspaceId, NodeId = s.NodeId, Kind = Name(s.Kind),
        ChangeJson = Ser(s.Change), ResultJson = Ser(s.Result), CreatedAt = Utc(s.CreatedAt),
    };

    public static Scenario ToDomain(ScenarioRow r) => new(
        r.Id, r.WorkspaceId, r.NodeId, Parse<ScenarioKind>(r.Kind), De<ScenarioChange>(r.ChangeJson), De<ScenarioResult>(r.ResultJson), r.CreatedAt);

    public static FactorRow ToRow(Factor f) => new()
    {
        Id = f.Id, Kind = Name(f.Kind), Key = f.Key, Value = f.Value, Unit = f.Unit, Source = f.Source, Year = f.Year, RowLabel = f.RowLabel,
    };

    public static Factor ToDomain(FactorRow r) => new(r.Id, Parse<FactorKind>(r.Kind), r.Key, r.Value, r.Unit, r.Source, r.Year, r.RowLabel);

    public static LaneRow ToRow(Lane l) => new()
    {
        Id = l.Id, Origin = l.Origin, Dest = l.Dest, ModeDefault = Name(l.ModeDefault),
        DistanceKmJson = Ser(ByName(l.DistanceKm)), LeadDaysJson = Ser(ByName(l.LeadDaysByMode)), CostPerKgJson = Ser(ByName(l.CostPerKgByMode)), Note = l.Note,
    };

    public static Lane ToDomain(LaneRow r) => new(
        r.Id, r.Origin, r.Dest, Parse<TransportMode>(r.ModeDefault),
        ByMode<decimal>(De<Dictionary<string, decimal?>>(r.DistanceKmJson)),
        ByMode<int>(De<Dictionary<string, int?>>(r.LeadDaysJson)),
        ByMode<decimal>(De<Dictionary<string, decimal?>>(r.CostPerKgJson)),
        r.Note);

    public static AlternativeRow ToRow(Alternative a) => new()
    {
        Id = a.Id, NodeCategory = a.NodeCategory, Label = a.Label, Country = a.Country, LaneId = a.LaneId, CostEur = a.CostEur, MassT = a.MassT,
    };

    public static Alternative ToDomain(AlternativeRow r) => new(r.Id, r.NodeCategory, r.Label, r.Country, r.LaneId, r.CostEur, r.MassT);

    public static BuyerRow ToRow(Guid workspaceId, Buyer b) => new() { WorkspaceId = workspaceId, Name = b.Name, Cui = b.Cui, Country = b.Country, City = b.City };

    public static Buyer ToDomain(BuyerRow r) => new(r.Name, r.Cui, r.Country, r.City);

    private static Dictionary<string, T?> ByName<T>(IReadOnlyDictionary<TransportMode, T?> source) where T : struct =>
        source.ToDictionary(kv => kv.Key.ToString().ToLowerInvariant(), kv => kv.Value);

    private static IReadOnlyDictionary<TransportMode, T?> ByMode<T>(Dictionary<string, T?> source) where T : struct
    {
        var result = new Dictionary<TransportMode, T?>();
        foreach (var mode in Enum.GetValues<TransportMode>())
        {
            result[mode] = source.TryGetValue(mode.ToString().ToLowerInvariant(), out var value) ? value : null;
        }
        return result;
    }
}
