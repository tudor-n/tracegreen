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

/// <summary><see cref="Correction"/>: the buyer set the category for this description (F6); it wins over every other source.</summary>
public enum ClassificationSource { Invoice, Cache, Model, Stub, Correction }

public enum FactorKind { Spend, Transport, Material, Grid }
