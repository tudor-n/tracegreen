using Microsoft.EntityFrameworkCore;
using Npgsql;
using TraceGreen.Domain.Model;
using TraceGreen.Domain.Ports;
using TraceGreen.Domain.Seed;

namespace TraceGreen.Infrastructure.Postgres;

/// <summary>
/// Postgres repository. Every write runs in a transaction that first takes a per-workspace advisory lock, so audit
/// entries are appended strictly in sequence; the unique indexes on <c>token_nonce</c> and
/// <c>(workspace_id, prev_hash)</c> are the safety net if a second instance ever skips the lock.
/// </summary>
public sealed class EfRepository(IDbContextFactory<TraceGreenDbContext> factory) : IRepository
{
    public string Mode => "postgres";

    public async Task<HealthStatus> HealthAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return new HealthStatus(true, null);
        }
        catch (Exception ex)
        {
            return new HealthStatus(false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public async Task<Buyer> GetBuyerAsync(Ctx ctx, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Buyers.AsNoTracking().FirstOrDefaultAsync(b => b.WorkspaceId == ctx.WorkspaceId, ct)
            ?? throw new InvalidOperationException("Workspace has no buyer; seed first.");
        return Mapping.ToDomain(row);
    }

    public async Task<Invoice?> GetInvoiceAsync(Ctx ctx, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.WorkspaceId == ctx.WorkspaceId && i.Id == id, ct);
        return row is null ? null : Mapping.ToDomain(row);
    }

    public async Task<Invoice?> GetInvoiceByRawHashAsync(Ctx ctx, string rawXmlHash, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.WorkspaceId == ctx.WorkspaceId && i.RawXmlHash == rawXmlHash, ct);
        return row is null ? null : Mapping.ToDomain(row);
    }

    public async Task<SupplierNode?> GetNodeAsync(Ctx ctx, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.WorkspaceId == ctx.WorkspaceId && n.Id == id, ct);
        return row is null ? null : Mapping.ToDomain(row);
    }

    public async Task<SupplierNode?> FindNodeAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        return row is null ? null : Mapping.ToDomain(row);
    }

    public async Task<IReadOnlyList<SupplierNode>> ListNodesAsync(Ctx ctx, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Nodes.AsNoTracking().Where(n => n.WorkspaceId == ctx.WorkspaceId)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToListAsync(ct);
        return rows.Select(Mapping.ToDomain).ToList();
    }

    public async Task<AuditEntry> InsertInvoiceWithNodeAsync(Ctx ctx, Invoice invoice, SupplierNode node, AuditBuilder audit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(db, ctx.WorkspaceId, ct);
        db.Invoices.Add(Mapping.ToRow(invoice));
        db.Nodes.Add(Mapping.ToRow(node));
        var row = await StageAuditAsync(db, ctx.WorkspaceId, audit, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Mapping.ToDomain(row);
    }

    public async Task<RecordVerificationResult> RecordVerificationAsync(Ctx ctx, Verification verification, NodePatch patch, AuditBuilder audit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(db, ctx.WorkspaceId, ct);

        if (await db.Verifications.AnyAsync(v => v.TokenNonce == verification.TokenNonce, ct))
        {
            return new RecordVerificationResult(RecordVerificationStatus.AlreadyUsed, null, null);
        }
        var nodeRow = await db.Nodes.FirstOrDefaultAsync(n => n.WorkspaceId == ctx.WorkspaceId && n.Id == verification.NodeId, ct)
            ?? throw new InvalidOperationException($"Node {verification.NodeId} not found.");
        Mapping.Apply(nodeRow, patch);
        db.Verifications.Add(Mapping.ToRow(verification));
        var auditRow = await StageAuditAsync(db, ctx.WorkspaceId, audit, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return new RecordVerificationResult(RecordVerificationStatus.AlreadyUsed, null, null);
        }
        await tx.CommitAsync(ct);
        return new RecordVerificationResult(RecordVerificationStatus.Ok, Mapping.ToDomain(nodeRow), Mapping.ToDomain(auditRow));
    }

    public async Task<IReadOnlyList<Verification>> ListVerificationsAsync(Ctx ctx, Guid nodeId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Verifications.AsNoTracking().Where(v => v.WorkspaceId == ctx.WorkspaceId && v.NodeId == nodeId)
            .OrderBy(v => v.CreatedAt).ToListAsync(ct);
        return rows.Select(Mapping.ToDomain).ToList();
    }

    public async Task<AuditEntry> AppendAuditAsync(Ctx ctx, AuditBuilder build, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(db, ctx.WorkspaceId, ct);
        var row = await StageAuditAsync(db, ctx.WorkspaceId, build, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Mapping.ToDomain(row);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAuditAsync(Ctx ctx, long afterId, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.AuditLog.AsNoTracking().Where(a => a.WorkspaceId == ctx.WorkspaceId && a.Id > afterId)
            .OrderBy(a => a.Id).Take(limit).ToListAsync(ct);
        return rows.Select(Mapping.ToDomain).ToList();
    }

    public async Task SaveScenarioAsync(Ctx ctx, Scenario scenario, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Scenarios.Add(Mapping.ToRow(scenario));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Scenario>> ListScenariosAsync(Ctx ctx, Guid? nodeId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Scenarios.AsNoTracking()
            .Where(s => s.WorkspaceId == ctx.WorkspaceId && (nodeId == null || s.NodeId == nodeId))
            .OrderBy(s => s.CreatedAt).ToListAsync(ct);
        return rows.Select(Mapping.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Factor>> ListFactorsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Factors.AsNoTracking().OrderBy(f => f.Id).ToListAsync(ct)).Select(Mapping.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Lane>> ListLanesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Lanes.AsNoTracking().OrderBy(l => l.Id).ToListAsync(ct)).Select(Mapping.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Alternative>> ListAlternativesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Alternatives.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct)).Select(Mapping.ToDomain).ToList();
    }

    public async Task SeedAsync(SeedBundle bundle, bool reset, CancellationToken ct)
    {
        var data = DemoState.Build(bundle);
        var workspaceId = bundle.DemoState.WorkspaceId;

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(db, workspaceId, ct);

        // Reference data is replaced wholesale; it carries no history.
        await db.Factors.ExecuteDeleteAsync(ct);
        await db.Lanes.ExecuteDeleteAsync(ct);
        await db.Alternatives.ExecuteDeleteAsync(ct);
        db.Factors.AddRange(bundle.Factors.Select(Mapping.ToRow));
        db.Lanes.AddRange(bundle.Lanes.Select(Mapping.ToRow));
        db.Alternatives.AddRange(bundle.Alternatives.Select(Mapping.ToRow));
        var buyer = await db.Buyers.FirstOrDefaultAsync(b => b.WorkspaceId == workspaceId, ct);
        if (buyer is null)
        {
            db.Buyers.Add(Mapping.ToRow(workspaceId, bundle.Buyer));
        }
        else
        {
            buyer.Name = bundle.Buyer.Name;
            buyer.Cui = bundle.Buyer.Cui;
            buyer.Country = bundle.Buyer.Country;
            buyer.City = bundle.Buyer.City;
        }
        await db.SaveChangesAsync(ct);

        if (reset)
        {
            // Row triggers keep audit_log immutable; TRUNCATE is statement-level and is the one sanctioned reset path (single-tenant demo).
            await db.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE invoices, nodes, verifications, scenarios, audit_log RESTART IDENTITY", ct);
        }

        var alreadySeeded = await db.AuditLog.AnyAsync(a => a.WorkspaceId == workspaceId, ct)
            || await db.Nodes.AnyAsync(n => n.WorkspaceId == workspaceId, ct);
        if (!alreadySeeded)
        {
            db.Invoices.AddRange(data.Invoices.Select(Mapping.ToRow));
            db.Nodes.AddRange(data.Nodes.Select(Mapping.ToRow));
            db.Verifications.AddRange(data.Verifications.Select(Mapping.ToRow));
            await db.SaveChangesAsync(ct);
            foreach (var entry in data.Audit)
            {
                db.AuditLog.Add(Mapping.ToRow(entry));
                await db.SaveChangesAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    private static async Task LockWorkspaceAsync(TraceGreenDbContext db, Guid workspaceId, CancellationToken ct)
    {
        var key = BitConverter.ToInt64(workspaceId.ToByteArray(), 0);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [key], ct);
    }

    /// <summary>Builds the next entry from the current chain head and adds it; the id is assigned on SaveChanges.</summary>
    private static async Task<AuditRow> StageAuditAsync(TraceGreenDbContext db, Guid workspaceId, AuditBuilder build, CancellationToken ct)
    {
        var last = await db.AuditLog.AsNoTracking().Where(a => a.WorkspaceId == workspaceId).OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        var row = Mapping.ToRow(build(last is null ? null : Mapping.ToDomain(last)));
        db.AuditLog.Add(row);
        return row;
    }
}
