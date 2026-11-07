using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>
/// The one-line reason behind a node's category (F6): the invoice code, the cache's or a model's rationale. Nullable,
/// because rows written before it existed carry none.
/// </summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261107090000_ClassificationRationale")]
public sealed class ClassificationRationale : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "classification_rationale", table: "nodes", type: "text", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "classification_rationale", table: "nodes");
}
