using TraceGreen.Domain.Model;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Domain.Ports;

public sealed record HealthStatus(bool Ok, string? Detail);

public enum RecordVerificationStatus { Ok, AlreadyUsed }

public sealed record RecordVerificationResult(RecordVerificationStatus Status, SupplierNode? Node, AuditEntry? Entry);

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

    Task<RecordVerificationResult> RecordVerificationAsync(Ctx ctx, Verification verification, NodePatch patch, AuditBuilder audit, CancellationToken ct);

    Task<IReadOnlyList<Verification>> ListVerificationsAsync(Ctx ctx, Guid nodeId, CancellationToken ct);

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
