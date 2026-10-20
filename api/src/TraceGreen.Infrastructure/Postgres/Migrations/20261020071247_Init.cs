using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alternatives",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    node_category = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    lane_id = table.Column<string>(type: "text", nullable: false),
                    cost_eur = table.Column<decimal>(type: "numeric", nullable: false),
                    mass_t = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alternatives", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    prev_hash = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "buyers",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    cui = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buyers", x => x.workspace_id);
                });

            migrationBuilder.CreateTable(
                name: "factors",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<decimal>(type: "numeric", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    row_label = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_factors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_name = table.Column<string>(type: "text", nullable: false),
                    vendor_cui = table.Column<string>(type: "text", nullable: false),
                    buyer_cui = table.Column<string>(type: "text", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    raw_xml_hash = table.Column<string>(type: "text", nullable: false),
                    invoice_hash = table.Column<string>(type: "text", nullable: false),
                    extracted_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lanes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    dest = table.Column<string>(type: "text", nullable: false),
                    mode_default = table.Column<string>(type: "text", nullable: false),
                    distance_km_json = table.Column<string>(type: "jsonb", nullable: false),
                    lead_days_json = table.Column<string>(type: "jsonb", nullable: false),
                    cost_per_kg_json = table.Column<string>(type: "jsonb", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lanes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    category_id = table.Column<string>(type: "text", nullable: false),
                    sector_id = table.Column<string>(type: "text", nullable: false),
                    ai_confidence = table.Column<double>(type: "double precision", nullable: true),
                    classification_source = table.Column<string>(type: "text", nullable: false),
                    mass_t = table.Column<decimal>(type: "numeric", nullable: true),
                    lane_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    spend_kg = table.Column<decimal>(type: "numeric", nullable: false),
                    activity_kg = table.Column<decimal>(type: "numeric", nullable: true),
                    spend_eur = table.Column<decimal>(type: "numeric", nullable: false),
                    cost_eur = table.Column<decimal>(type: "numeric", nullable: false),
                    lead_days = table.Column<int>(type: "integer", nullable: false),
                    transport_mode = table.Column<string>(type: "text", nullable: true),
                    energy_source = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nodes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scenarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    change_json = table.Column<string>(type: "jsonb", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scenarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_nonce = table.Column<string>(type: "text", nullable: false),
                    transport_mode = table.Column<string>(type: "text", nullable: true),
                    energy_source = table.Column<string>(type: "text", nullable: true),
                    mass_tconfirmed = table.Column<decimal>(type: "numeric", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_workspace_id_prev_hash",
                table: "audit_log",
                columns: new[] { "workspace_id", "prev_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_workspace_id_raw_xml_hash",
                table: "invoices",
                columns: new[] { "workspace_id", "raw_xml_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_nodes_workspace_id",
                table: "nodes",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_scenarios_workspace_id_node_id",
                table: "scenarios",
                columns: new[] { "workspace_id", "node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_verifications_token_nonce",
                table: "verifications",
                column: "token_nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_verifications_workspace_id_node_id",
                table: "verifications",
                columns: new[] { "workspace_id", "node_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alternatives");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "buyers");

            migrationBuilder.DropTable(
                name: "factors");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "lanes");

            migrationBuilder.DropTable(
                name: "nodes");

            migrationBuilder.DropTable(
                name: "scenarios");

            migrationBuilder.DropTable(
                name: "verifications");
        }
    }
}
