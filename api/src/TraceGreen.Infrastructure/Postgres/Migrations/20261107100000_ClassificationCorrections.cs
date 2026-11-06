using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>
/// The buyer's corrections of a line's category (F6), one per workspace and description key, with row-level security
/// enabled like the other workspace tables (C8). The demo reset truncates it.
/// </summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261107100000_ClassificationCorrections")]
public sealed class ClassificationCorrections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "classification_corrections",
            columns: table => new
            {
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                cache_key = table.Column<string>(type: "text", nullable: false),
                category_id = table.Column<string>(type: "text", nullable: false),
                node_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_classification_corrections", x => new { x.workspace_id, x.cache_key }));
        migrationBuilder.Sql("ALTER TABLE classification_corrections ENABLE ROW LEVEL SECURITY;");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "classification_corrections");
}
