using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>
/// The classification review (F6): whether a person still has to look at a node's category, and when one last did.
/// Nodes stored before it get the state <c>NodeFactory.InitialReview</c> would have given them: <c>Pending</c> for an
/// AI-mapped category (source <c>Cache</c> or <c>Model</c>), <c>NotNeeded</c> otherwise.
/// </summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261106150000_ClassificationReview")]
public sealed class NodeClassificationReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "review", table: "nodes", type: "text", nullable: false, defaultValue: "NotNeeded");
        migrationBuilder.AddColumn<DateTimeOffset>(name: "reviewed_at", table: "nodes", type: "timestamp with time zone", nullable: true);
        migrationBuilder.Sql("UPDATE nodes SET review = 'Pending' WHERE classification_source IN ('Cache', 'Model');");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "reviewed_at", table: "nodes");
        migrationBuilder.DropColumn(name: "review", table: "nodes");
    }
}
