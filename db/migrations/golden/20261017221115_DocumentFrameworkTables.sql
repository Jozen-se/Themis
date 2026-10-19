START TRANSACTION;
DO $$ BEGIN IF EXISTS (SELECT 1 FROM kernel.outbox_event) THEN RAISE EXCEPTION 'kernel.outbox_event holds rows written before the D-50 event envelope. Recreate the database before running Themis.Migrator (step 7 brief).'; END IF; END $$;

DROP INDEX kernel.ux_item_company_item_company;

ALTER TABLE kernel.outbox_event ADD aggregate_type character varying(40) NOT NULL;

ALTER TABLE kernel.outbox_event ADD command_name character varying(81) NOT NULL;

ALTER TABLE kernel.outbox_event ADD company_id uuid;

ALTER TABLE kernel.outbox_event ADD correlation_id character varying(128) NOT NULL;

ALTER TABLE kernel.item_company ADD CONSTRAINT ak_item_company_item_company UNIQUE (item_id, company_id);

CREATE TABLE kernel.document_type (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    number_assigned character varying(20) NOT NULL,
    gapless boolean NOT NULL,
    posts_inventory boolean NOT NULL,
    posts_general_ledger boolean NOT NULL,
    party_role character varying(40),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_document_type PRIMARY KEY (id),
    CONSTRAINT ak_document_type_code UNIQUE (code),
    CONSTRAINT ak_document_type_code_gapless UNIQUE (code, gapless),
    CONSTRAINT ck_document_type_number_assigned CHECK (number_assigned IN ('OnCreate', 'OnPost', 'NumberingOnly')),
    CONSTRAINT ck_document_type_numbering_only CHECK (number_assigned <> 'NumberingOnly' OR (posts_inventory = false AND posts_general_ledger = false AND party_role IS NULL))
);

CREATE TABLE kernel.document (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    document_type character varying(40) NOT NULL,
    status character varying(20) NOT NULL,
    document_number character varying(40),
    draft_reference character varying(40) NOT NULL,
    document_date date NOT NULL,
    posting_date date,
    currency character varying(3) NOT NULL,
    warehouse_id uuid,
    external_reference character varying(200),
    posted_at timestamp with time zone,
    posted_by uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_document PRIMARY KEY (id),
    CONSTRAINT ak_document_id_company_id UNIQUE (id, company_id),
    CONSTRAINT ck_document_status CHECK (status IN ('Draft', 'Posted', 'Discarded', 'Reversed')),
    CONSTRAINT fk_document_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_document_currency FOREIGN KEY (currency) REFERENCES kernel.currency (code) ON DELETE RESTRICT,
    CONSTRAINT fk_document_type FOREIGN KEY (document_type) REFERENCES kernel.document_type (code) ON DELETE RESTRICT,
    CONSTRAINT fk_document_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse (id, company_id) ON DELETE RESTRICT
);

CREATE TABLE kernel.document_transition (
    id uuid NOT NULL,
    document_type character varying(40) NOT NULL,
    command character varying(40) NOT NULL,
    from_status character varying(20) NOT NULL,
    to_status character varying(20) NOT NULL,
    event_name character varying(81) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_document_transition PRIMARY KEY (id),
    CONSTRAINT ck_document_transition_from_status CHECK (from_status IN ('Draft', 'Posted', 'Discarded', 'Reversed')),
    CONSTRAINT ck_document_transition_to_status CHECK (to_status IN ('Draft', 'Posted', 'Discarded', 'Reversed')),
    CONSTRAINT fk_document_transition_type FOREIGN KEY (document_type) REFERENCES kernel.document_type (code) ON DELETE RESTRICT
);

CREATE TABLE kernel.number_series (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    document_type character varying(40) NOT NULL,
    scope character varying(20) NOT NULL,
    fiscal_year_id uuid,
    prefix character varying(40),
    padding integer NOT NULL,
    next_number bigint NOT NULL,
    gapless boolean NOT NULL,
    type_requires_gapless boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_number_series PRIMARY KEY (id),
    CONSTRAINT ck_number_series_fiscal_year CHECK ((scope = 'PerFiscalYear') = (fiscal_year_id IS NOT NULL)),
    CONSTRAINT ck_number_series_gapless CHECK (type_requires_gapless = false OR gapless = true),
    CONSTRAINT ck_number_series_journal_entry CHECK (document_type <> 'JOURNAL_ENTRY' OR (gapless = true AND scope = 'PerFiscalYear')),
    CONSTRAINT ck_number_series_next_number CHECK (next_number >= 1),
    CONSTRAINT ck_number_series_padding CHECK (padding BETWEEN 0 AND 20),
    CONSTRAINT ck_number_series_scope CHECK (scope IN ('Continuous', 'PerFiscalYear')),
    CONSTRAINT fk_number_series_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_number_series_fiscal_year_company FOREIGN KEY (fiscal_year_id, company_id) REFERENCES kernel.fiscal_year (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_number_series_type_gapless FOREIGN KEY (document_type, type_requires_gapless) REFERENCES kernel.document_type (code, gapless) ON DELETE RESTRICT
);

CREATE TABLE kernel.document_line (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    document_id uuid NOT NULL,
    line_number integer NOT NULL,
    line_type character varying(20) NOT NULL,
    item_id uuid,
    description character varying(200) NOT NULL,
    quantity numeric(20,6),
    unit_code character varying(40),
    quantity_base numeric(20,6),
    warehouse_id uuid,
    stock_status character varying(20),
    entered_unit_cost numeric(28,10),
    unit_cost numeric(28,10),
    cost_amount numeric(20,4),
    line_status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_document_line PRIMARY KEY (id),
    CONSTRAINT ak_document_line_id_company_id UNIQUE (id, company_id),
    CONSTRAINT ck_document_line_entered_unit_cost CHECK (entered_unit_cost IS NULL OR entered_unit_cost >= 0),
    CONSTRAINT ck_document_line_item_line CHECK (line_type <> 'Item' OR (item_id IS NOT NULL AND quantity IS NOT NULL AND unit_code IS NOT NULL AND quantity_base IS NOT NULL AND warehouse_id IS NOT NULL AND stock_status IS NOT NULL)),
    CONSTRAINT ck_document_line_line_number CHECK (line_number > 0),
    CONSTRAINT ck_document_line_line_status CHECK (line_status IN ('Open', 'Closed', 'Cancelled')),
    CONSTRAINT ck_document_line_line_type CHECK (line_type IN ('Item', 'Charge', 'Account', 'Allocation', 'Text')),
    CONSTRAINT ck_document_line_quantity CHECK (quantity IS NULL OR quantity <> 0),
    CONSTRAINT ck_document_line_stock_status CHECK (stock_status IS NULL OR stock_status IN ('Available', 'Blocked')),
    CONSTRAINT fk_document_line_document_company FOREIGN KEY (document_id, company_id) REFERENCES kernel.document (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_document_line_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company (item_id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_document_line_unit FOREIGN KEY (unit_code) REFERENCES kernel.unit_of_measure (code) ON DELETE RESTRICT,
    CONSTRAINT fk_document_line_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse (id, company_id) ON DELETE RESTRICT
);

CREATE TABLE kernel.inventory_adjustment (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    reason_code character varying(20),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_inventory_adjustment PRIMARY KEY (id),
    CONSTRAINT ck_inventory_adjustment_reason_code CHECK (reason_code IS NULL OR reason_code IN ('OpeningBalance', 'CountDifference', 'Scrap', 'Other')),
    CONSTRAINT fk_inventory_adjustment_document_company FOREIGN KEY (id, company_id) REFERENCES kernel.document (id, company_id) ON DELETE RESTRICT
);

CREATE INDEX "IX_outbox_event_company_id" ON kernel.outbox_event (company_id);

CREATE INDEX "IX_document_currency" ON kernel.document (currency);

CREATE INDEX "IX_document_document_type" ON kernel.document (document_type);

CREATE INDEX "IX_document_warehouse_id_company_id" ON kernel.document (warehouse_id, company_id);

CREATE INDEX ix_document_company_type_status ON kernel.document (company_id, document_type, status);

CREATE UNIQUE INDEX ux_document_company_type_number ON kernel.document (company_id, document_type, document_number) WHERE document_number IS NOT NULL;

CREATE INDEX "IX_document_line_document_id_company_id" ON kernel.document_line (document_id, company_id);

CREATE INDEX "IX_document_line_item_id_company_id" ON kernel.document_line (item_id, company_id);

CREATE INDEX "IX_document_line_unit_code" ON kernel.document_line (unit_code);

CREATE INDEX "IX_document_line_warehouse_id_company_id" ON kernel.document_line (warehouse_id, company_id);

CREATE UNIQUE INDEX ux_document_line_document_number ON kernel.document_line (document_id, line_number);

CREATE UNIQUE INDEX ux_document_transition_type_command_from ON kernel.document_transition (document_type, command, from_status);

CREATE UNIQUE INDEX "IX_inventory_adjustment_id_company_id" ON kernel.inventory_adjustment (id, company_id);

CREATE INDEX "IX_number_series_document_type_type_requires_gapless" ON kernel.number_series (document_type, type_requires_gapless);

CREATE INDEX "IX_number_series_fiscal_year_id_company_id" ON kernel.number_series (fiscal_year_id, company_id);

CREATE UNIQUE INDEX ux_number_series_company_type_year ON kernel.number_series (company_id, document_type, fiscal_year_id) NULLS NOT DISTINCT;

ALTER TABLE kernel.outbox_event ADD CONSTRAINT fk_outbox_event_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT;

GRANT SELECT ON "kernel"."document_type" TO "themis_kernel";

GRANT SELECT ON "kernel"."document_transition" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."document" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."document_line" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."inventory_adjustment" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."number_series" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261017221115_DocumentFrameworkTables', '10.0.13');

COMMIT;
