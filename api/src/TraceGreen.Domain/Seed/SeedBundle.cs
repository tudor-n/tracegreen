using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Seed;

public sealed record ClassificationMapEntry(string ListId, string Prefix, string CategoryId);

public sealed record CachedClassification(string CategoryId, double Confidence, string Rationale, string Source, string? Description);

public sealed record DemoNodeSpec(
    string Key,
    Guid NodeId,
    Guid InvoiceId,
    string InvoiceFile,
    string LaneId,
    bool Present,
    NodeStatus Status,
    TransportMode? TransportMode,
    EnergySource? EnergySource,
    Guid? VerificationId,
    string? Nonce);

public sealed record DemoStateSpec(Guid WorkspaceId, IReadOnlyList<DemoNodeSpec> Nodes);

/// <summary>Everything under <c>seed/</c>, validated. Shared by both repositories and by the demo endpoints.</summary>
public sealed record SeedBundle(
    string Directory,
    Buyer Buyer,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Factor> Factors,
    IReadOnlyList<Lane> Lanes,
    IReadOnlyList<Alternative> Alternatives,
    IReadOnlyList<ClassificationMapEntry> ClassificationMap,
    IReadOnlyDictionary<string, CachedClassification> AiCache,
    DemoStateSpec DemoState)
{
    private IReadOnlySet<string>? _allowList;

    /// <summary>Category ids the AI may return; anything else becomes UNCLASSIFIED.</summary>
    public IReadOnlySet<string> AllowList => _allowList ??= Categories.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

    public string FilePath(string relative) => Path.Combine(Directory, relative);

    public Category CategoryOf(string categoryId) =>
        Categories.FirstOrDefault(c => c.Id == categoryId)
        ?? Categories.First(c => c.Id == Ids.Unclassified);
}
