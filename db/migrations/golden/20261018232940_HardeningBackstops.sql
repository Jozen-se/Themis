START TRANSACTION;
ALTER TABLE kernel.stock_balance ADD CONSTRAINT ck_stock_balance_on_hand CHECK (on_hand_base >= 0);

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

ALTER TABLE kernel.fiscal_period ADD CONSTRAINT ex_fiscal_period_no_overlap EXCLUDE USING gist (company_id WITH =, daterange(start_date, end_date, '[]') WITH &&);

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

ALTER FUNCTION kernel.journal_entry_balance_check() OWNER TO themis_migrator;

CREATE CONSTRAINT TRIGGER ct_journal_entry_balanced AFTER INSERT ON kernel.journal_entry DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION kernel.journal_entry_balance_check();

CREATE CONSTRAINT TRIGGER ct_journal_line_balanced AFTER INSERT ON kernel.journal_line DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION kernel.journal_entry_balance_check();

REVOKE ALL ON FUNCTION kernel.journal_entry_balance_check() FROM PUBLIC;

REVOKE ALL ON FUNCTION kernel.journal_entry_balance_check() FROM themis_migrator;

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261018232940_HardeningBackstops', '10.0.13');

COMMIT;
