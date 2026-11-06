using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>
/// The categories a buyer chose in a classification review (F6), by the <c>CacheKey</c> of the line description, so a later
/// line with the same description gets the buyer's category before the AI is asked. Workspace state like the nodes: row-level
/// security as in <c>Rls</c>, and the demo reset truncates it.
/// </summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261106160000_BuyerClassifications")]
public sealed class BuyerClassifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "buyer_classifications",
            columns: table => new
            {
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                line_key = table.Column<string>(type: "text", nullable: false),
                category_id = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_buyer_classifications", x => new { x.workspace_id, x.line_key }));
        migrationBuilder.Sql("ALTER TABLE buyer_classifications ENABLE ROW LEVEL SECURITY;");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "buyer_classifications");
}
