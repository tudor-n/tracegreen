using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Ports;

public sealed record HealthStatus(bool Ok, string? Detail);

/// <summary><see cref="NodeChanged"/>: the node no longer matches the caller's <see cref="NodeAnswerState"/>; nothing was written.</summary>
public enum RecordVerificationStatus { Ok, AlreadyUsed, NodeChanged }

public sealed record RecordVerificationResult(RecordVerificationStatus Status, SupplierNode? Node, AuditEntry? Entry);

/// <summary>
/// What a caller saw of a node when it built an answer: its status and confirmed answers. Passed to
/// <see cref="IRepository.RecordVerificationAsync"/>, it is compared under the write's lock, so an answer built on a node
/// that someone else answered in the meantime is refused instead of overwriting that answer.
/// </summary>
public sealed record NodeAnswerState(NodeStatus Status, TransportMode? TransportMode, EnergySource? EnergySource)
{
    public static NodeAnswerState Of(SupplierNode node) => new(node.Status, node.TransportMode, node.EnergySource);
}

/// <summary>
/// <see cref="NodeNotFound"/>: no such node in the workspace (a reset can remove one). <see cref="CategoryChanged"/>: the
/// node's category is no longer the one the caller expected; nothing was written.
/// </summary>
public enum ReviewClassificationStatus { Ok, NodeNotFound, CategoryChanged }

/// <summary><see cref="Node"/> is the node after the write, or as it is when the category changed.</summary>
public sealed record ReviewClassificationResult(ReviewClassificationStatus Status, SupplierNode? Node, AuditEntry? Entry);

/// <summary><see cref="Refused"/>: the caller's updater refused, as the node no longer matches what its page showed; nothing was written.</summary>
public enum UpdateTrustStatus { Ok, NodeNotFound, Refused }

/// <summary><see cref="Node"/> is the node after the write, or as it is when the write was refused.</summary>
public sealed record UpdateTrustResult(UpdateTrustStatus Status, SupplierNode? Node, AuditEntry? Entry);

public enum InsertInvoiceStatus { Ok, Duplicate }

/// <summary><see cref="ExistingNodeId"/> is the node that already holds the invoice when <see cref="Status"/> is Duplicate.</summary>
public sealed record InsertInvoiceResult(InsertInvoiceStatus Status, AuditEntry? Entry, Guid? ExistingNodeId);

/// <summary>
/// Persistence port. Write methods that touch several rows are atomic in every adapter, and the audit builder is
/// invoked under the adapter's lock or transaction so the chain can never fork. Single use of a verification
/// token is enforced here through the unique nonce, not by the signer.
/// </summary>
public interface IRepository
{
    string Mode { get; }

    Task<HealthStatus> HealthAsync(CancellationToken ct);

    Task<Buyer> GetBuyerAsync(Ctx ctx, CancellationToken ct);

    Task<Invoice?> GetInvoiceAsync(Ctx ctx, Guid id, CancellationToken ct);

    Task<Invoice?> GetInvoiceByRawHashAsync(Ctx ctx, string rawXmlHash, CancellationToken ct);

    Task<SupplierNode?> GetNodeAsync(Ctx ctx, Guid id, CancellationToken ct);

    /// <summary>Looks a node up by id across workspaces; used by the anonymous supplier flow, which only has a token.</summary>
    Task<SupplierNode?> FindNodeAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<SupplierNode>> ListNodesAsync(Ctx ctx, CancellationToken ct);

    /// <summary>
    /// Refuses, under the same lock as the write, an invoice whose raw hash or id is already stored or a node id already
    /// taken, so a duplicate cannot slip in between a caller's own check and the insert.
    /// </summary>
    Task<InsertInvoiceResult> InsertInvoiceWithNodeAsync(Ctx ctx, Invoice invoice, SupplierNode node, AuditBuilder audit, CancellationToken ct);

    /// <param name="patch">Called under the write's lock with the node as stored, after the nonce and expected-state checks, and before <paramref name="audit"/>.</param>
    /// <param name="expected">
    /// When set, the write happens only if the node still has this status and these answers; otherwise
    /// <see cref="RecordVerificationStatus.NodeChanged"/> with nothing written. Null writes whatever the node holds (the phone page).
    /// </param>
    Task<RecordVerificationResult> RecordVerificationAsync(
        Ctx ctx, Verification verification, VerificationPatcher patch, AuditBuilder audit, NodeAnswerState? expected, CancellationToken ct);

    Task<IReadOnlyList<Verification>> ListVerificationsAsync(Ctx ctx, Guid nodeId, CancellationToken ct);

    /// <summary>
    /// A person's classification review (F6), atomic with its audit entry. Under the write's lock the node is read, its
    /// category compared with <paramref name="expectedCategoryId"/> (else <see cref="ReviewClassificationStatus.CategoryChanged"/>
    /// with nothing written), and <paramref name="review"/> computes the patch and the audit entry from that node. The
    /// category is also remembered for the write's <see cref="ClassificationWrite.LineKey"/>, replacing an earlier one.
    /// </summary>
    Task<ReviewClassificationResult> ReviewClassificationAsync(
        Ctx ctx, Guid nodeId, string expectedCategoryId, ClassificationReviewer review, CancellationToken ct);

    /// <summary>
    /// A change to a node's trust inputs or target (F7: the buyer's records, a person's decision on a claim, the target),
    /// atomic with its audit entry. Under the write's lock the node is read and handed to <paramref name="update"/>, which
    /// computes the write from it (the counted figure included) or refuses it.
    /// </summary>
    Task<UpdateTrustResult> UpdateTrustAsync(Ctx ctx, Guid nodeId, TrustUpdater update, CancellationToken ct);

    /// <summary>The buyer's remembered categories for those of <paramref name="lineKeys"/> that have one, by line key. A reset forgets them.</summary>
    Task<IReadOnlyDictionary<string, string>> ListBuyerClassificationsAsync(Ctx ctx, IReadOnlyCollection<string> lineKeys, CancellationToken ct);

    Task<AuditEntry> AppendAuditAsync(Ctx ctx, AuditBuilder build, CancellationToken ct);

    /// <summary>Entries in ascending id order, starting after <paramref name="afterId"/>.</summary>
    Task<IReadOnlyList<AuditEntry>> ListAuditAsync(Ctx ctx, long afterId, int limit, CancellationToken ct);

    Task SaveScenarioAsync(Ctx ctx, Scenario scenario, CancellationToken ct);

    Task<IReadOnlyList<Scenario>> ListScenariosAsync(Ctx ctx, Guid? nodeId, CancellationToken ct);

    Task<IReadOnlyList<Factor>> ListFactorsAsync(CancellationToken ct);

    Task<IReadOnlyList<Lane>> ListLanesAsync(CancellationToken ct);

    Task<IReadOnlyList<Alternative>> ListAlternativesAsync(CancellationToken ct);

    /// <summary>Loads reference data and the demo start state. Idempotent; <paramref name="reset"/> wipes the demo workspace first.</summary>
    Task SeedAsync(SeedBundle bundle, bool reset, CancellationToken ct);
}
