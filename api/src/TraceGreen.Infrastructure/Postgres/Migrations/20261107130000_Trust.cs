using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>
/// The trust model (F7): what a node's trust assessment reads (the supplier's evidence, how the answer arrived, the buyer's
/// records, a person's decisions per claim) and its target as jsonb, the counted figure, and the evidence of each
/// verification. All nullable: rows written before it carry none, and a verified node without a counted figure counts its
/// activity figure as claimed until the demo is reset.
/// </summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261107130000_Trust")]
public sealed class Trust : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "trust_json", table: "nodes", type: "jsonb", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "counted_kg", table: "nodes", type: "numeric", nullable: true);
        migrationBuilder.AddColumn<string>(name: "target_json", table: "nodes", type: "jsonb", nullable: true);
        migrationBuilder.AddColumn<string>(name: "evidence_json", table: "verifications", type: "jsonb", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "evidence_json", table: "verifications");
        migrationBuilder.DropColumn(name: "target_json", table: "nodes");
        migrationBuilder.DropColumn(name: "counted_kg", table: "nodes");
        migrationBuilder.DropColumn(name: "trust_json", table: "nodes");
    }
}
