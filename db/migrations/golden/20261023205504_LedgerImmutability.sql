START TRANSACTION;
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

ALTER FUNCTION kernel.ledger_row_immutable() OWNER TO themis_migrator;

CREATE TRIGGER tr_journal_entry_immutable BEFORE UPDATE OR DELETE ON kernel.journal_entry FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_journal_entry_no_truncate BEFORE TRUNCATE ON kernel.journal_entry FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_journal_line_immutable BEFORE UPDATE OR DELETE ON kernel.journal_line FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_journal_line_no_truncate BEFORE TRUNCATE ON kernel.journal_line FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_inventory_entry_immutable BEFORE UPDATE OR DELETE ON kernel.inventory_entry FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_inventory_entry_no_truncate BEFORE TRUNCATE ON kernel.inventory_entry FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_audit_event_immutable BEFORE UPDATE OR DELETE ON kernel.audit_event FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_audit_event_no_truncate BEFORE TRUNCATE ON kernel.audit_event FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_setting_value_immutable BEFORE UPDATE OR DELETE ON kernel.setting_value FOR EACH ROW EXECUTE FUNCTION kernel.ledger_row_immutable();

CREATE TRIGGER tr_setting_value_no_truncate BEFORE TRUNCATE ON kernel.setting_value FOR EACH STATEMENT EXECUTE FUNCTION kernel.ledger_row_immutable();

REVOKE ALL ON FUNCTION kernel.ledger_row_immutable() FROM PUBLIC;

REVOKE ALL ON FUNCTION kernel.ledger_row_immutable() FROM themis_migrator;

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261023205504_LedgerImmutability', '10.0.13');

COMMIT;
