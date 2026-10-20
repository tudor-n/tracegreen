using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TraceGreen.Infrastructure.Postgres;

public sealed partial class TraceGreenDbContext(DbContextOptions<TraceGreenDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceRow> Invoices => Set<InvoiceRow>();
    public DbSet<NodeRow> Nodes => Set<NodeRow>();
    public DbSet<VerificationRow> Verifications => Set<VerificationRow>();
    public DbSet<AuditRow> AuditLog => Set<AuditRow>();
    public DbSet<ScenarioRow> Scenarios => Set<ScenarioRow>();
    public DbSet<FactorRow> Factors => Set<FactorRow>();
    public DbSet<LaneRow> Lanes => Set<LaneRow>();
    public DbSet<AlternativeRow> Alternatives => Set<AlternativeRow>();
    public DbSet<BuyerRow> Buyers => Set<BuyerRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<InvoiceRow>(e =>
        {
            e.ToTable("invoices");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WorkspaceId, x.RawXmlHash });
            e.Property(x => x.ExtractedJson).HasColumnType("jsonb");
        });
        model.Entity<NodeRow>(e =>
        {
            e.ToTable("nodes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.WorkspaceId);
        });
        model.Entity<VerificationRow>(e =>
        {
            e.ToTable("verifications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenNonce).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.NodeId });
        });
        model.Entity<AuditRow>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.WorkspaceId, x.PrevHash }).IsUnique();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
        });
        model.Entity<ScenarioRow>(e =>
        {
            e.ToTable("scenarios");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WorkspaceId, x.NodeId });
            e.Property(x => x.ChangeJson).HasColumnType("jsonb");
            e.Property(x => x.ResultJson).HasColumnType("jsonb");
        });
        model.Entity<FactorRow>(e => { e.ToTable("factors"); e.HasKey(x => x.Id); });
        model.Entity<LaneRow>(e =>
        {
            e.ToTable("lanes");
            e.HasKey(x => x.Id);
            e.Property(x => x.DistanceKmJson).HasColumnType("jsonb");
            e.Property(x => x.LeadDaysJson).HasColumnType("jsonb");
            e.Property(x => x.CostPerKgJson).HasColumnType("jsonb");
        });
        model.Entity<AlternativeRow>(e => { e.ToTable("alternatives"); e.HasKey(x => x.Id); });
        model.Entity<BuyerRow>(e => { e.ToTable("buyers"); e.HasKey(x => x.WorkspaceId); });

        // snake_case columns, keys and indexes so the schema reads naturally in psql and on Supabase.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }
            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    private static string ToSnakeCase(string name) =>
        SnakeBoundary().Replace(name, "_$1").ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex SnakeBoundary();
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without a running host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TraceGreenDbContext>
{
    public TraceGreenDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TraceGreenDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=tracegreen;Username=tracegreen;Password=tracegreen")
            .Options);
}
