using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>What document an invoice was imported from (F9): "ubl", or "pdf" after a person confirmed its fields.</summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261107110000_DocumentKind")]
public sealed class DocumentKind : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "document_kind", table: "invoices", type: "text", nullable: false, defaultValue: "ubl");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "document_kind", table: "invoices");
}
