START TRANSACTION;
ALTER TABLE kernel.audit_event ADD command_id uuid;

CREATE TABLE kernel.journal_entry (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    entry_number character varying(40) NOT NULL,
    fiscal_period_id uuid NOT NULL,
    posting_date date NOT NULL,
    document_date date NOT NULL,
    source_document_id uuid NOT NULL,
    command_id uuid NOT NULL,
    description character varying(200) NOT NULL,
    reversal_of_entry_id uuid,
    recorded_at timestamp with time zone NOT NULL,
    recorded_by uuid NOT NULL,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_journal_entry PRIMARY KEY (id),
    CONSTRAINT ak_journal_entry_id_company_id UNIQUE (id, company_id),
    CONSTRAINT fk_journal_entry_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_entry_fiscal_period FOREIGN KEY (fiscal_period_id) REFERENCES kernel.fiscal_period (id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_entry_reversal_of FOREIGN KEY (reversal_of_entry_id) REFERENCES kernel.journal_entry (id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_entry_source_document_company FOREIGN KEY (source_document_id, company_id) REFERENCES kernel.document (id, company_id) ON DELETE RESTRICT
);

CREATE TABLE kernel.inventory_entry (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    item_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    lot_id uuid,
    stock_status character varying(20) NOT NULL,
    movement_type character varying(20) NOT NULL,
    quantity_base numeric(20,6) NOT NULL,
    cost_amount numeric(20,4) NOT NULL,
    unit_cost numeric(28,10) NOT NULL,
    posting_date date NOT NULL,
    document_date date NOT NULL,
    source_line_id uuid NOT NULL,
    journal_entry_id uuid,
    reversal_of_entry_id uuid,
    command_id uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    recorded_by uuid NOT NULL,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_inventory_entry PRIMARY KEY (id),
    CONSTRAINT ck_inventory_entry_cost_sign CHECK (cost_amount = 0 OR sign(cost_amount) = sign(quantity_base)),
    CONSTRAINT ck_inventory_entry_journal_entry CHECK (cost_amount = 0 OR journal_entry_id IS NOT NULL),
    CONSTRAINT ck_inventory_entry_lot_id CHECK (lot_id IS NULL),
    CONSTRAINT ck_inventory_entry_movement_type CHECK (movement_type IN ('AdjustmentIn', 'AdjustmentOut', 'Issue', 'ReturnIn', 'StatusOut', 'StatusIn')),
    CONSTRAINT ck_inventory_entry_quantity CHECK (quantity_base <> 0),
    CONSTRAINT ck_inventory_entry_stock_status CHECK (stock_status IN ('Available', 'Blocked')),
    CONSTRAINT fk_inventory_entry_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_inventory_entry_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company (item_id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_inventory_entry_journal_entry_company FOREIGN KEY (journal_entry_id, company_id) REFERENCES kernel.journal_entry (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_inventory_entry_reversal_of FOREIGN KEY (reversal_of_entry_id) REFERENCES kernel.inventory_entry (id) ON DELETE RESTRICT,
    CONSTRAINT fk_inventory_entry_source_line_company FOREIGN KEY (source_line_id, company_id) REFERENCES kernel.document_line (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_inventory_entry_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse (id, company_id) ON DELETE RESTRICT
);

CREATE TABLE kernel.journal_line (
    id uuid NOT NULL,
    journal_entry_id uuid NOT NULL,
    company_id uuid NOT NULL,
    line_number integer NOT NULL,
    account_id uuid NOT NULL,
    account_role character varying(40),
    posting_rule_id uuid,
    amount numeric(20,4) NOT NULL,
    txn_currency character varying(3) NOT NULL,
    txn_amount numeric(20,4) NOT NULL,
    item_id uuid,
    warehouse_id uuid,
    source_line_id uuid,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_journal_line PRIMARY KEY (id),
    CONSTRAINT ck_journal_line_amount CHECK (amount <> 0),
    CONSTRAINT ck_journal_line_line_number CHECK (line_number > 0),
    CONSTRAINT ck_journal_line_txn_sign CHECK (sign(txn_amount) = sign(amount)),
    CONSTRAINT fk_journal_line_account FOREIGN KEY (account_id) REFERENCES kernel.account (id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_line_entry_company FOREIGN KEY (journal_entry_id, company_id) REFERENCES kernel.journal_entry (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_line_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company (item_id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_line_posting_rule FOREIGN KEY (posting_rule_id) REFERENCES kernel.posting_profile_rule (id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_line_source_line_company FOREIGN KEY (source_line_id, company_id) REFERENCES kernel.document_line (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_line_txn_currency FOREIGN KEY (txn_currency) REFERENCES kernel.currency (code) ON DELETE RESTRICT,
    CONSTRAINT fk_journal_line_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse (id, company_id) ON DELETE RESTRICT
);

CREATE TABLE kernel.item_cost_state (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    item_id uuid NOT NULL,
    quantity_base numeric(20,6) NOT NULL,
    inventory_value numeric(20,4) NOT NULL,
    average_cost numeric(28,10),
    last_inventory_entry_id uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_item_cost_state PRIMARY KEY (id),
    CONSTRAINT ck_item_cost_state_quantity CHECK (quantity_base >= 0),
    CONSTRAINT ck_item_cost_state_value CHECK (inventory_value >= 0 AND (quantity_base <> 0 OR inventory_value = 0)),
    CONSTRAINT fk_item_cost_state_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company (item_id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_item_cost_state_last_entry FOREIGN KEY (last_inventory_entry_id) REFERENCES kernel.inventory_entry (id) ON DELETE RESTRICT
);

CREATE TABLE kernel.stock_balance (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    item_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    lot_id uuid,
    stock_status character varying(20) NOT NULL,
    on_hand_base numeric(20,6) NOT NULL,
    reserved_base numeric(20,6) NOT NULL,
    last_inventory_entry_id uuid,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_stock_balance PRIMARY KEY (id),
    CONSTRAINT ck_stock_balance_lot_id CHECK (lot_id IS NULL),
    CONSTRAINT ck_stock_balance_reserved CHECK (reserved_base >= 0),
    CONSTRAINT ck_stock_balance_stock_status CHECK (stock_status IN ('Available', 'Blocked')),
    CONSTRAINT fk_stock_balance_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company (item_id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_stock_balance_last_entry FOREIGN KEY (last_inventory_entry_id) REFERENCES kernel.inventory_entry (id) ON DELETE RESTRICT,
    CONSTRAINT fk_stock_balance_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse (id, company_id) ON DELETE RESTRICT
);

CREATE INDEX "IX_inventory_entry_item_id_company_id" ON kernel.inventory_entry (item_id, company_id);

CREATE INDEX "IX_inventory_entry_journal_entry_id_company_id" ON kernel.inventory_entry (journal_entry_id, company_id);

CREATE INDEX "IX_inventory_entry_reversal_of_entry_id" ON kernel.inventory_entry (reversal_of_entry_id);

CREATE INDEX "IX_inventory_entry_source_line_id_company_id" ON kernel.inventory_entry (source_line_id, company_id);

CREATE INDEX "IX_inventory_entry_warehouse_id_company_id" ON kernel.inventory_entry (warehouse_id, company_id);

CREATE INDEX ix_inventory_entry_balance_key ON kernel.inventory_entry (company_id, item_id, warehouse_id, stock_status);

CREATE INDEX ix_inventory_entry_journal_entry ON kernel.inventory_entry (journal_entry_id);

CREATE INDEX "IX_item_cost_state_item_id_company_id" ON kernel.item_cost_state (item_id, company_id);

CREATE INDEX "IX_item_cost_state_last_inventory_entry_id" ON kernel.item_cost_state (last_inventory_entry_id);

CREATE UNIQUE INDEX ux_item_cost_state_company_item ON kernel.item_cost_state (company_id, item_id);

CREATE INDEX "IX_journal_entry_fiscal_period_id" ON kernel.journal_entry (fiscal_period_id);

CREATE INDEX "IX_journal_entry_reversal_of_entry_id" ON kernel.journal_entry (reversal_of_entry_id);

CREATE INDEX "IX_journal_entry_source_document_id_company_id" ON kernel.journal_entry (source_document_id, company_id);

CREATE INDEX ix_journal_entry_company_posting_date ON kernel.journal_entry (company_id, posting_date);

CREATE UNIQUE INDEX ux_journal_entry_company_number ON kernel.journal_entry (company_id, entry_number);

CREATE INDEX "IX_journal_line_item_id_company_id" ON kernel.journal_line (item_id, company_id);

CREATE INDEX "IX_journal_line_journal_entry_id_company_id" ON kernel.journal_line (journal_entry_id, company_id);

CREATE INDEX "IX_journal_line_posting_rule_id" ON kernel.journal_line (posting_rule_id);

CREATE INDEX "IX_journal_line_source_line_id_company_id" ON kernel.journal_line (source_line_id, company_id);

CREATE INDEX "IX_journal_line_txn_currency" ON kernel.journal_line (txn_currency);

CREATE INDEX "IX_journal_line_warehouse_id_company_id" ON kernel.journal_line (warehouse_id, company_id);

CREATE INDEX ix_journal_line_account ON kernel.journal_line (account_id);

CREATE UNIQUE INDEX ux_journal_line_entry_number ON kernel.journal_line (journal_entry_id, line_number);

CREATE INDEX "IX_stock_balance_item_id_company_id" ON kernel.stock_balance (item_id, company_id);

CREATE INDEX "IX_stock_balance_last_inventory_entry_id" ON kernel.stock_balance (last_inventory_entry_id);

CREATE INDEX "IX_stock_balance_warehouse_id_company_id" ON kernel.stock_balance (warehouse_id, company_id);

CREATE UNIQUE INDEX ux_stock_balance_key ON kernel.stock_balance (company_id, item_id, warehouse_id, lot_id, stock_status) NULLS NOT DISTINCT;

ALTER TABLE kernel.audit_event ADD CONSTRAINT ck_audit_event_command_id CHECK (command_id IS NOT NULL) NOT VALID;

GRANT SELECT, INSERT ON "kernel"."journal_entry" TO "themis_kernel";

GRANT SELECT, INSERT ON "kernel"."journal_line" TO "themis_kernel";

GRANT SELECT, INSERT ON "kernel"."inventory_entry" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."item_cost_state" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."stock_balance" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261018115001_PostingTables', '10.0.13');

COMMIT;
