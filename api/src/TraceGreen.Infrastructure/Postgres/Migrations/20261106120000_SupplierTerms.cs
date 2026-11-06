using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>
/// The supplier terms a verification was accepted under (F3): their version and SHA-256. Nullable, because the seed's
/// verifications and any row written before the terms existed carry none.
/// </summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261106120000_SupplierTerms")]
public sealed class SupplierTerms : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "terms_version", table: "verifications", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "terms_sha256", table: "verifications", type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "terms_sha256", table: "verifications");
        migrationBuilder.DropColumn(name: "terms_version", table: "verifications");
    }
}
