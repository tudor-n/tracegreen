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

/// <summary>
/// Serialised in camelCase. <see cref="Buyer"/> is a category a person on the dashboard confirmed or chose
/// (<c>POST /api/nodes/{nodeId}/classification</c>); it is not AI-mapped.
/// </summary>
public enum ClassificationSource { Invoice, Cache, Model, Stub, Buyer }

/// <summary>
/// Whether a person has looked at the node's category (F6). Serialised "notNeeded" | "pending" | "confirmed" | "corrected".
/// An AI-mapped category waits as <see cref="Pending"/>, whatever its confidence; an invoice code or the stub needs no review.
/// <see cref="Confirmed"/>: a person kept the category the classifier gave. <see cref="Corrected"/>: a person changed it,
/// and it stays corrected through later reviews.
/// </summary>
public enum ClassificationReview { NotNeeded, Pending, Confirmed, Corrected }

public enum FactorKind { Spend, Transport, Material, Grid }
