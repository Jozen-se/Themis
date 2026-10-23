using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 2b, the ledger immutability backstop (D-60, RR-63; I-14, R-LED-06). On journal_entry, journal_line,
/// inventory_entry, audit_event and setting_value a BEFORE UPDATE OR DELETE row trigger and a BEFORE TRUNCATE statement
/// trigger refuse the change, so the table owner is stopped as well as the application roles, which the grants already
/// stop. Each raises SQLSTATE 23514 with constraint name ct_&lt;table&gt;_immutable and a message naming the table and D-60,
/// so the pipeline never retries it (D-41) nor maps it (RR-45). The trigger function is SECURITY DEFINER, owned by
/// themis_migrator with a fixed search_path, and no role holds EXECUTE on it. A later migration that must change such
/// rows disables and re-enables the triggers inside its own script and needs the owner's review. A superuser stays
/// outside the guarantee. Every statement is literal text, fixed once merged (RR-39).
/// </summary>
internal sealed partial class LedgerImmutability : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // D-60: one function for every guarded table; the constraint name and the message name the table it fired on.
        migrationBuilder.Sql(
            """
            CREATE FUNCTION kernel.ledger_row_immutable() RETURNS trigger
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = pg_catalog, kernel
            AS $$
            BEGIN
                RAISE EXCEPTION 'kernel.% is insert-only: % refused (D-60, R-LED-06)', TG_TABLE_NAME, TG_OP
                    USING ERRCODE = '23514', CONSTRAINT = 'ct_' || TG_TABLE_NAME || '_immutable', TABLE = TG_TABLE_NAME, SCHEMA = TG_TABLE_SCHEMA;
                RETURN NULL;
            END
            $$;
            """);
        migrationBuilder.Sql("ALTER FUNCTION kernel.ledger_row_immutable() OWNER TO themis_migrator;");
        migrationBuilder.Sql("CREATE TRIGGER tr_journal_entry_immutable BEFORE UPDATE OR DELETE ON kernel.journal_entry FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_journal_entry_no_truncate BEFORE TRUNCATE ON kernel.journal_entry FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_journal_line_immutable BEFORE UPDATE OR DELETE ON kernel.journal_line FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_journal_line_no_truncate BEFORE TRUNCATE ON kernel.journal_line FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_inventory_entry_immutable BEFORE UPDATE OR DELETE ON kernel.inventory_entry FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_inventory_entry_no_truncate BEFORE TRUNCATE ON kernel.inventory_entry FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_audit_event_immutable BEFORE UPDATE OR DELETE ON kernel.audit_event FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_audit_event_no_truncate BEFORE TRUNCATE ON kernel.audit_event FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_setting_value_immutable BEFORE UPDATE OR DELETE ON kernel.setting_value FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER tr_setting_value_no_truncate BEFORE TRUNCATE ON kernel.setting_value FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();");
        // Creating a trigger needs EXECUTE on its function; firing it does not. So the privilege is revoked last, from PUBLIC
        // and from the owner alike, leaving the function with an empty access list (as RR-47 did for its balance check).
        migrationBuilder.Sql("REVOKE ALL ON FUNCTION kernel.ledger_row_immutable() FROM PUBLIC;");
        migrationBuilder.Sql("REVOKE ALL ON FUNCTION kernel.ledger_row_immutable() FROM themis_migrator;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_setting_value_no_truncate ON kernel.setting_value;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_setting_value_immutable ON kernel.setting_value;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_audit_event_no_truncate ON kernel.audit_event;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_audit_event_immutable ON kernel.audit_event;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_inventory_entry_no_truncate ON kernel.inventory_entry;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_inventory_entry_immutable ON kernel.inventory_entry;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_journal_line_no_truncate ON kernel.journal_line;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_journal_line_immutable ON kernel.journal_line;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_journal_entry_no_truncate ON kernel.journal_entry;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_journal_entry_immutable ON kernel.journal_entry;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS kernel.ledger_row_immutable();");
    }
}
