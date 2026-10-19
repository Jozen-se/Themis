START TRANSACTION;
CREATE TABLE kernel.account (
    id uuid NOT NULL,
    chart_id uuid NOT NULL,
    account_number character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    account_type character varying(20) NOT NULL,
    is_control boolean NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_account PRIMARY KEY (id),
    CONSTRAINT ak_account_id_is_control UNIQUE (id, is_control),
    CONSTRAINT ck_account_account_type CHECK (account_type IN ('Asset', 'Liability', 'Equity', 'Revenue', 'Expense')),
    CONSTRAINT ck_account_status CHECK (status IN ('Active', 'Blocked', 'Inactive')),
    CONSTRAINT fk_account_chart FOREIGN KEY (chart_id) REFERENCES kernel.chart_of_accounts (id) ON DELETE RESTRICT
);

CREATE TABLE kernel.posting_profile_rule (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    account_role character varying(40) NOT NULL,
    item_posting_group character varying(40),
    party_posting_group character varying(20),
    ship_to_scope character varying(20),
    tax_code_id uuid,
    reason_code character varying(20),
    account_id uuid NOT NULL,
    expects_control boolean GENERATED ALWAYS AS (account_role IN ('INVENTORY', 'RECEIVABLES', 'OUTPUT_VAT')) STORED NOT NULL,
    valid_from date NOT NULL,
    valid_to date,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_posting_profile_rule PRIMARY KEY (id),
    CONSTRAINT ck_posting_profile_rule_account_role CHECK (account_role IN ('INVENTORY', 'COGS', 'REVENUE', 'RECEIVABLES', 'OUTPUT_VAT', 'BANK', 'PAYMENT_DIFFERENCE', 'REALISED_FX', 'ROUNDING', 'INVENTORY_ADJUSTMENT', 'VAT_SETTLEMENT', 'OPENING_BALANCE')),
    CONSTRAINT ck_posting_profile_rule_keys_by_role CHECK ((account_role = 'INVENTORY' AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'COGS' AND party_posting_group IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'REVENUE' AND party_posting_group IS NULL AND reason_code IS NULL) OR (account_role = 'RECEIVABLES' AND item_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'OUTPUT_VAT' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND reason_code IS NULL) OR (account_role = 'BANK' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'PAYMENT_DIFFERENCE' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'REALISED_FX' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'ROUNDING' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'INVENTORY_ADJUSTMENT' AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL) OR (account_role = 'VAT_SETTLEMENT' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL) OR (account_role = 'OPENING_BALANCE' AND item_posting_group IS NULL AND party_posting_group IS NULL AND ship_to_scope IS NULL AND tax_code_id IS NULL AND reason_code IS NULL)),
    CONSTRAINT ck_posting_profile_rule_party_posting_group CHECK (party_posting_group IS NULL OR party_posting_group IN ('Domestic', 'EU', 'NonEU')),
    CONSTRAINT ck_posting_profile_rule_reason_code CHECK (reason_code IS NULL OR reason_code IN ('OpeningBalance', 'CountDifference', 'Scrap', 'Other')),
    CONSTRAINT ck_posting_profile_rule_ship_to_scope CHECK (ship_to_scope IS NULL OR ship_to_scope IN ('Domestic', 'EU', 'NonEU')),
    CONSTRAINT ck_posting_profile_rule_validity CHECK (valid_to IS NULL OR valid_to > valid_from),
    CONSTRAINT fk_posting_profile_rule_account_control FOREIGN KEY (account_id, expects_control) REFERENCES kernel.account (id, is_control) ON DELETE RESTRICT,
    CONSTRAINT fk_posting_profile_rule_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_posting_profile_rule_item_posting_group FOREIGN KEY (item_posting_group) REFERENCES kernel.item_posting_group (code) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_account_chart_number ON kernel.account (chart_id, account_number);

CREATE INDEX "IX_posting_profile_rule_account_id_expects_control" ON kernel.posting_profile_rule (account_id, expects_control);

CREATE INDEX "IX_posting_profile_rule_item_posting_group" ON kernel.posting_profile_rule (item_posting_group);

CREATE INDEX ix_posting_profile_rule_company_role ON kernel.posting_profile_rule (company_id, account_role);

ALTER TABLE kernel.posting_profile_rule ADD CONSTRAINT ex_posting_profile_rule_no_overlap EXCLUDE USING gist (company_id WITH =, account_role WITH =, coalesce(item_posting_group, '') WITH =, coalesce(party_posting_group, '') WITH =, coalesce(ship_to_scope, '') WITH =, coalesce(tax_code_id, '00000000-0000-0000-0000-000000000000'::uuid) WITH =, coalesce(reason_code, '') WITH =, daterange(valid_from, valid_to, '[)') WITH &&);

GRANT SELECT, INSERT, UPDATE ON "kernel"."account" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."posting_profile_rule" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261013121553_GeneralLedgerTables', '10.0.13');

COMMIT;
