using System.Text.Json.Serialization;

namespace TraceGreen.Domain.Model;

/// <summary>Serialised in camelCase: "road" | "rail" | "sea" | "air".</summary>
public enum TransportMode { Road, Rail, Sea, Air }

/// <summary>Serialised in camelCase: "grid" | "renewable" | "unknown".</summary>
public enum EnergySource { Grid, Renewable, Unknown }

/// <summary>Serialised upper-case to match the UI contract.</summary>
public enum NodeStatus
{
    [JsonStringEnumMemberName("ESTIMATED")] Estimated,
    [JsonStringEnumMemberName("PARTIAL")] Partial,
    [JsonStringEnumMemberName("VERIFIED")] Verified,
}

public enum ClassificationSource { Invoice, Cache, Model, Stub }

public enum FactorKind { Spend, Transport, Material, Grid }
