START TRANSACTION;
CREATE TABLE kernel.chart_of_accounts (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    template character varying(200),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_chart_of_accounts PRIMARY KEY (id)
);

CREATE TABLE kernel.installation (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    status character varying(20) NOT NULL,
    default_locale character varying(35) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_installation PRIMARY KEY (id),
    CONSTRAINT ck_installation_status CHECK (status IN ('Active', 'Suspended'))
);

CREATE TABLE kernel.party (
    id uuid NOT NULL,
    party_number character varying(40) NOT NULL,
    party_type character varying(20) NOT NULL,
    name character varying(200) NOT NULL,
    organisation_number character varying(50),
    vat_number character varying(50),
    country_code character varying(2) NOT NULL,
    language character varying(35),
    status character varying(20) NOT NULL,
    duplicate_key character varying(400) NOT NULL,
    anonymised_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_party PRIMARY KEY (id),
    CONSTRAINT ck_party_party_type CHECK (party_type IN ('Organisation', 'Person')),
    CONSTRAINT ck_party_status CHECK (status IN ('Active', 'Blocked', 'Inactive'))
);

CREATE TABLE kernel.company (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    party_id uuid NOT NULL,
    legal_name character varying(200) NOT NULL,
    organisation_number character varying(50) NOT NULL,
    vat_number character varying(50),
    country_code character varying(2) NOT NULL,
    base_currency character varying(3) NOT NULL,
    time_zone character varying(64) NOT NULL,
    fiscal_year_start_month integer NOT NULL,
    chart_of_accounts_id uuid NOT NULL,
    rounding_mode character varying(20) NOT NULL,
    tax_rounding_level character varying(20) NOT NULL,
    payment_tolerance numeric(20,4),
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_company PRIMARY KEY (id),
    CONSTRAINT ck_company_fiscal_year_start_month CHECK (fiscal_year_start_month BETWEEN 1 AND 12),
    CONSTRAINT ck_company_rounding_mode CHECK (rounding_mode IN ('HalfAwayFromZero', 'HalfEven')),
    CONSTRAINT ck_company_status CHECK (status IN ('Active', 'Inactive')),
    CONSTRAINT ck_company_tax_rounding_level CHECK (tax_rounding_level IN ('Line', 'Document')),
    CONSTRAINT fk_company_chart_of_accounts FOREIGN KEY (chart_of_accounts_id) REFERENCES kernel.chart_of_accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_company_party FOREIGN KEY (party_id) REFERENCES kernel.party (id) ON DELETE RESTRICT
);

CREATE TABLE kernel.fiscal_year (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    code character varying(40) NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_fiscal_year PRIMARY KEY (id),
    CONSTRAINT ak_fiscal_year_id_company_id UNIQUE (id, company_id),
    CONSTRAINT ck_fiscal_year_dates CHECK (start_date <= end_date),
    CONSTRAINT ck_fiscal_year_status CHECK (status IN ('Open', 'Closed')),
    CONSTRAINT fk_fiscal_year_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT
);

CREATE TABLE kernel.site (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    address_id uuid,
    time_zone character varying(64),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_site PRIMARY KEY (id),
    CONSTRAINT ak_site_id_company_id UNIQUE (id, company_id),
    CONSTRAINT fk_site_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT
);

CREATE TABLE kernel.fiscal_period (
    id uuid NOT NULL,
    fiscal_year_id uuid NOT NULL,
    company_id uuid NOT NULL,
    period_number integer NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_fiscal_period PRIMARY KEY (id),
    CONSTRAINT ck_fiscal_period_dates CHECK (start_date <= end_date),
    CONSTRAINT ck_fiscal_period_number CHECK (period_number BETWEEN 1 AND 18),
    CONSTRAINT ck_fiscal_period_status CHECK (status IN ('Open', 'SoftClosed', 'Closed')),
    CONSTRAINT fk_fiscal_period_year_company FOREIGN KEY (fiscal_year_id, company_id) REFERENCES kernel.fiscal_year (id, company_id) ON DELETE RESTRICT
);

CREATE TABLE kernel.warehouse (
    id uuid NOT NULL,
    site_id uuid NOT NULL,
    company_id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    is_default boolean NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_warehouse PRIMARY KEY (id),
    CONSTRAINT ck_warehouse_status CHECK (status IN ('Active', 'Blocked', 'Inactive')),
    CONSTRAINT fk_warehouse_site_company FOREIGN KEY (site_id, company_id) REFERENCES kernel.site (id, company_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_chart_of_accounts_code ON kernel.chart_of_accounts (code);

CREATE INDEX "IX_company_chart_of_accounts_id" ON kernel.company (chart_of_accounts_id);

CREATE INDEX "IX_company_party_id" ON kernel.company (party_id);

CREATE UNIQUE INDEX ux_company_code ON kernel.company (code);

CREATE INDEX "IX_fiscal_period_fiscal_year_id_company_id" ON kernel.fiscal_period (fiscal_year_id, company_id);

CREATE INDEX ix_fiscal_period_company_start ON kernel.fiscal_period (company_id, start_date);

CREATE UNIQUE INDEX ux_fiscal_period_year_number ON kernel.fiscal_period (fiscal_year_id, period_number);

CREATE UNIQUE INDEX ux_fiscal_year_company_code ON kernel.fiscal_year (company_id, code);

CREATE UNIQUE INDEX ux_installation_code ON kernel.installation (code);

CREATE INDEX ix_party_duplicate_key ON kernel.party (duplicate_key);

CREATE UNIQUE INDEX ux_party_party_number ON kernel.party (party_number);

CREATE UNIQUE INDEX ux_site_company_code ON kernel.site (company_id, code);

CREATE INDEX "IX_warehouse_site_id_company_id" ON kernel.warehouse (site_id, company_id);

CREATE UNIQUE INDEX ux_warehouse_company_code ON kernel.warehouse (company_id, code);

CREATE UNIQUE INDEX ux_warehouse_site_default ON kernel.warehouse (site_id) WHERE is_default;

GRANT SELECT, INSERT, UPDATE ON "kernel"."installation" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."party" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."chart_of_accounts" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."company" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."site" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."warehouse" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."fiscal_year" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."fiscal_period" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261009142943_OrganisationTables', '10.0.13');

COMMIT;
