using TraceGreen.Domain.Audit;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Infrastructure.Memory;

/// <summary>
/// In-memory repository for tests and the offline fallback. One lock guards every mutation, so each write method is
/// atomic and the audit chain cannot fork.
/// </summary>
public sealed class MemoryRepository : IRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Workspace> _workspaces = new();
    private IReadOnlyList<Factor> _factors = [];
    private IReadOnlyList<Lane> _lanes = [];
    private IReadOnlyList<Alternative> _alternatives = [];

    public string Mode => "memory";

    public Task<HealthStatus> HealthAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, null));

    public Task<Buyer> GetBuyerAsync(Ctx ctx, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Get(ctx.WorkspaceId).Buyer ?? throw new InvalidOperationException("Workspace has no buyer; seed first."));
        }
    }

    public Task<Invoice?> GetInvoiceAsync(Ctx ctx, Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Get(ctx.WorkspaceId).Invoices.GetValueOrDefault(id));
        }
    }

    public Task<Invoice?> GetInvoiceByRawHashAsync(Ctx ctx, string rawXmlHash, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Get(ctx.WorkspaceId).Invoices.Values.FirstOrDefault(i => i.RawXmlHash == rawXmlHash));
        }
    }

    public Task<SupplierNode?> GetNodeAsync(Ctx ctx, Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Get(ctx.WorkspaceId).Nodes.GetValueOrDefault(id));
        }
    }

    public Task<SupplierNode?> FindNodeAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_workspaces.Values.Select(w => w.Nodes.GetValueOrDefault(id)).FirstOrDefault(n => n is not null));
        }
    }

    public Task<IReadOnlyList<SupplierNode>> ListNodesAsync(Ctx ctx, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SupplierNode>>(Get(ctx.WorkspaceId).Nodes.Values.OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToList());
        }
    }

    public Task<InsertInvoiceResult> InsertInvoiceWithNodeAsync(Ctx ctx, Invoice invoice, SupplierNode node, AuditBuilder audit, CancellationToken ct)
    {
        lock (_gate)
        {
            var ws = Get(ctx.WorkspaceId);
            var existing = ws.Invoices.Values.FirstOrDefault(i => i.RawXmlHash == invoice.RawXmlHash) ?? ws.Invoices.GetValueOrDefault(invoice.Id);
            if (existing is not null || ws.Nodes.ContainsKey(node.Id))
            {
                var existingNodeId = existing is null ? node.Id : ws.Nodes.Values.FirstOrDefault(n => n.InvoiceId == existing.Id)?.Id;
                return Task.FromResult(new InsertInvoiceResult(InsertInvoiceStatus.Duplicate, null, existingNodeId));
            }
            ws.Invoices[invoice.Id] = invoice;
            ws.Nodes[node.Id] = node;
            return Task.FromResult(new InsertInvoiceResult(InsertInvoiceStatus.Ok, ws.Append(audit), null));
        }
    }

    public Task<RecordVerificationResult> RecordVerificationAsync(Ctx ctx, Verification verification, NodePatch patch, AuditBuilder audit, CancellationToken ct)
    {
        lock (_gate)
        {
            var ws = Get(ctx.WorkspaceId);
            if (!ws.Nonces.Add(verification.TokenNonce))
            {
                return Task.FromResult(new RecordVerificationResult(RecordVerificationStatus.AlreadyUsed, null, null));
            }
            var node = ws.Nodes.GetValueOrDefault(verification.NodeId)
                ?? throw new InvalidOperationException($"Node {verification.NodeId} not found.");
            var patched = node with
            {
                Status = patch.Status,
                ActivityKg = patch.ActivityKg,
                TransportMode = patch.TransportMode,
                EnergySource = patch.EnergySource,
                MassT = patch.MassT ?? node.MassT,
                CostEur = patch.CostEur,
                LeadDays = patch.LeadDays,
            };
            ws.Nodes[node.Id] = patched;
            ws.Verifications[verification.Id] = verification;
            var entry = ws.Append(audit);
            return Task.FromResult(new RecordVerificationResult(RecordVerificationStatus.Ok, patched, entry));
        }
    }

    public Task<IReadOnlyList<Verification>> ListVerificationsAsync(Ctx ctx, Guid nodeId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Verification>>(
                Get(ctx.WorkspaceId).Verifications.Values.Where(v => v.NodeId == nodeId).OrderBy(v => v.CreatedAt).ToList());
        }
    }

    public Task<AuditEntry> AppendAuditAsync(Ctx ctx, AuditBuilder build, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Get(ctx.WorkspaceId).Append(build));
        }
    }

    public Task<IReadOnlyList<AuditEntry>> ListAuditAsync(Ctx ctx, long afterId, int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AuditEntry>>(
                Get(ctx.WorkspaceId).Audit.Where(a => a.Id > afterId).Take(limit).ToList());
        }
    }

    public Task SaveScenarioAsync(Ctx ctx, Scenario scenario, CancellationToken ct)
    {
        lock (_gate)
        {
            Get(ctx.WorkspaceId).Scenarios[scenario.Id] = scenario;
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<Scenario>> ListScenariosAsync(Ctx ctx, Guid? nodeId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Scenario>>(
                Get(ctx.WorkspaceId).Scenarios.Values.Where(s => nodeId is null || s.NodeId == nodeId).OrderBy(s => s.CreatedAt).ToList());
        }
    }

    public Task<IReadOnlyList<Factor>> ListFactorsAsync(CancellationToken ct) => Task.FromResult(_factors);

    public Task<IReadOnlyList<Lane>> ListLanesAsync(CancellationToken ct) => Task.FromResult(_lanes);

    public Task<IReadOnlyList<Alternative>> ListAlternativesAsync(CancellationToken ct) => Task.FromResult(_alternatives);

    public Task SeedAsync(SeedBundle bundle, bool reset, CancellationToken ct)
    {
        var data = DemoState.Build(bundle);
        lock (_gate)
        {
            _factors = bundle.Factors.ToList();
            _lanes = bundle.Lanes.ToList();
            _alternatives = bundle.Alternatives.ToList();
            var ws = Get(bundle.DemoState.WorkspaceId);
            if (reset)
            {
                ws.Clear();
            }
            ws.Buyer = bundle.Buyer;
            if (ws.Audit.Count > 0 || ws.Nodes.Count > 0)
            {
                return Task.CompletedTask;
            }
            foreach (var invoice in data.Invoices) ws.Invoices[invoice.Id] = invoice;
            foreach (var node in data.Nodes) ws.Nodes[node.Id] = node;
            foreach (var verification in data.Verifications)
            {
                ws.Verifications[verification.Id] = verification;
                ws.Nonces.Add(verification.TokenNonce);
            }
            foreach (var entry in data.Audit)
            {
                ws.Audit.Add(AuditChain.WithId(entry, ++ws.LastAuditId));
            }
        }
        return Task.CompletedTask;
    }

    private Workspace Get(Guid workspaceId)
    {
        if (!_workspaces.TryGetValue(workspaceId, out var ws))
        {
            ws = new Workspace();
            _workspaces[workspaceId] = ws;
        }
        return ws;
    }

    private sealed class Workspace
    {
        public Buyer? Buyer;
        public readonly Dictionary<Guid, Invoice> Invoices = new();
        public readonly Dictionary<Guid, SupplierNode> Nodes = new();
        public readonly Dictionary<Guid, Verification> Verifications = new();
        public readonly HashSet<string> Nonces = new(StringComparer.Ordinal);
        public readonly List<AuditEntry> Audit = new();
        public readonly Dictionary<Guid, Scenario> Scenarios = new();
        public long LastAuditId;

        public AuditEntry Append(AuditBuilder build)
        {
            var entry = AuditChain.WithId(build(Audit.Count == 0 ? null : Audit[^1]), ++LastAuditId);
            Audit.Add(entry);
            return entry;
        }

        public void Clear()
        {
            Invoices.Clear();
            Nodes.Clear();
            Verifications.Clear();
            Nonces.Clear();
            Audit.Clear();
            Scenarios.Clear();
            LastAuditId = 0;
        }
    }
}
