using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraceGreen.Infrastructure.Postgres.Migrations;

/// <summary>
/// Defence in depth (C4, C8): row-level security on tenant tables (the application connects as the table owner and
/// bypasses it; any other role such as a hosted anon role sees nothing until policies exist) and a trigger that makes
/// audit_log append-only. Resetting the demo uses TRUNCATE, which row triggers do not intercept.
/// </summary>
[DbContext(typeof(TraceGreenDbContext))]
[Migration("20261020235959_Rls")]
public sealed class Rls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE invoices ENABLE ROW LEVEL SECURITY;
            ALTER TABLE nodes ENABLE ROW LEVEL SECURITY;
            ALTER TABLE verifications ENABLE ROW LEVEL SECURITY;
            ALTER TABLE scenarios ENABLE ROW LEVEL SECURITY;
            ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;

            CREATE OR REPLACE FUNCTION audit_log_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'audit_log is append-only';
            END
            $$;

            DROP TRIGGER IF EXISTS audit_log_no_update ON audit_log;
            CREATE TRIGGER audit_log_no_update
                BEFORE UPDATE OR DELETE ON audit_log
                FOR EACH ROW EXECUTE FUNCTION audit_log_immutable();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS audit_log_no_update ON audit_log;
            DROP FUNCTION IF EXISTS audit_log_immutable();
            ALTER TABLE audit_log DISABLE ROW LEVEL SECURITY;
            ALTER TABLE scenarios DISABLE ROW LEVEL SECURITY;
            ALTER TABLE verifications DISABLE ROW LEVEL SECURITY;
            ALTER TABLE nodes DISABLE ROW LEVEL SECURITY;
            ALTER TABLE invoices DISABLE ROW LEVEL SECURITY;
            """);
    }
}
