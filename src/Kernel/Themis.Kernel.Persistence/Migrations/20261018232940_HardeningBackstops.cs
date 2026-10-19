using Microsoft.EntityFrameworkCore.Migrations;

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 1, the database backstops of the code review (Remediation Register, Code review 2026-10-18).
/// RR-44: an exclusion constraint keeps a company's fiscal periods disjoint; the migration stops with a message on
/// a database that already holds overlapping periods. RR-47: a CHECK keeps stock on hand at or above 0 (I-03,
/// R-INV-03; relaxed only if per-warehouse negative stock is ever allowed, D-09), and deferred constraint triggers
/// reject at commit a journal entry with fewer than two lines or whose lines do not sum to 0, and a journal line whose
/// entry then does not sum to 0 (I-01, R-LED-02). The trigger function is SECURITY DEFINER, owned by themis_migrator
/// with a fixed search_path, and no role holds EXECUTE on it (a trigger fires without that privilege); it raises one
/// fixed SQLSTATE of its own, 23514, so the pipeline never retries it (D-41) nor maps it (RR-45, RR-46). The
/// backstops stand behind code that checks first; every statement is literal text, fixed once merged (RR-39).
/// </summary>
internal sealed partial class HardeningBackstops : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // RR-47 (I-03, R-INV-03): the database refuses negative stock on hand.
        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_balance_on_hand",
            schema: "kernel",
            table: "stock_balance",
            sql: "on_hand_base >= 0");

        // RR-44: periods of one company never overlap; a database that already holds overlapping periods is reported, not repaired.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM kernel.fiscal_period a
                    JOIN kernel.fiscal_period b ON b.company_id = a.company_id AND b.id <> a.id
                        AND daterange(a.start_date, a.end_date, '[]') && daterange(b.start_date, b.end_date, '[]'))
                THEN
                    RAISE EXCEPTION 'kernel.fiscal_period holds overlapping periods of one company; resolve them before this migration adds the no-overlap constraint (RR-44).';
                END IF;
            END $$;
            """);
        migrationBuilder.Sql("ALTER TABLE kernel.fiscal_period ADD CONSTRAINT ex_fiscal_period_no_overlap EXCLUDE USING gist (company_id WITH =, daterange(start_date, end_date, '[]') WITH &&);");

        // RR-47 (I-01, R-LED-02): the balance check at commit. One function for both triggers; it re-sums the entry per row,
        // served by ux_journal_line_entry_number on (journal_entry_id, line_number), acceptable until the line limit of RR-54.
        migrationBuilder.Sql(
            """
            CREATE FUNCTION kernel.journal_entry_balance_check() RETURNS trigger
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = pg_catalog, kernel
            AS $$
            DECLARE
                v_entry uuid;
                v_lines integer;
                v_sum numeric;
            BEGIN
                -- Two statements, not one expression: PL/pgSQL prepares an expression as a whole, and NEW has only one of the two fields.
                IF TG_TABLE_NAME = 'journal_entry' THEN
                    v_entry := NEW.id;
                ELSE
                    v_entry := NEW.journal_entry_id;
                END IF;
                SELECT count(*), coalesce(sum(amount), 0) INTO v_lines, v_sum FROM kernel.journal_line WHERE journal_entry_id = v_entry;
                IF v_lines < 2 THEN
                    RAISE EXCEPTION 'journal entry % has fewer than 2 lines at commit (I-01, R-LED-02)', v_entry
                        USING ERRCODE = '23514', CONSTRAINT = 'ct_journal_entry_balanced';
                END IF;
                IF v_sum <> 0 THEN
                    RAISE EXCEPTION 'journal entry % does not sum to 0 at commit (I-01, R-LED-02)', v_entry
                        USING ERRCODE = '23514', CONSTRAINT = 'ct_journal_entry_balanced';
                END IF;
                RETURN NULL;
            END
            $$;
            """);
        migrationBuilder.Sql("ALTER FUNCTION kernel.journal_entry_balance_check() OWNER TO themis_migrator;");
        migrationBuilder.Sql("CREATE CONSTRAINT TRIGGER ct_journal_entry_balanced AFTER INSERT ON kernel.journal_entry DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION kernel.journal_entry_balance_check();");
        migrationBuilder.Sql("CREATE CONSTRAINT TRIGGER ct_journal_line_balanced AFTER INSERT ON kernel.journal_line DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION kernel.journal_entry_balance_check();");
        // Creating a trigger needs EXECUTE on its function; firing it does not. So the privilege is revoked last, from PUBLIC
        // and from the owner alike, leaving the function with an empty access list (checked by the I-14 function test).
        migrationBuilder.Sql("REVOKE ALL ON FUNCTION kernel.journal_entry_balance_check() FROM PUBLIC;");
        migrationBuilder.Sql("REVOKE ALL ON FUNCTION kernel.journal_entry_balance_check() FROM themis_migrator;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS ct_journal_line_balanced ON kernel.journal_line;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS ct_journal_entry_balanced ON kernel.journal_entry;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS kernel.journal_entry_balance_check();");
        migrationBuilder.Sql("ALTER TABLE kernel.fiscal_period DROP CONSTRAINT IF EXISTS ex_fiscal_period_no_overlap;");
        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_balance_on_hand",
            schema: "kernel",
            table: "stock_balance");
    }
}
