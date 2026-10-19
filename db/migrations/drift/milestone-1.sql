-- A database built by the Themis.Migrator of the git tag milestone-1 (RR-39, Build Brief, Migrations): the schema-only
-- pg_dump with ACLs, followed by the data that migrator run wrote (the rows the migrations seed, the system catalogues the
-- migrator applies and the migration history) as INSERT statements. The audit events of that run are left out: they
-- carry no command id, which the audit check constraint refuses on a restore, and the catalog comparison does not need
-- them. The drift test restores the file into a bare database, runs the head migrator on it and compares the PostgreSQL
-- catalog with a database built from scratch. Taken with pg_dump 16 on 2026-10-18; the psql \restrict guard lines are
-- removed so that the file runs through Npgsql.

--
-- PostgreSQL database dump
--


-- Dumped from database version 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1)
-- Dumped by pg_dump version 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: kernel; Type: SCHEMA; Schema: -; Owner: themis_migrator
--

CREATE SCHEMA kernel;


ALTER SCHEMA kernel OWNER TO themis_migrator;

--
-- Name: reporting; Type: SCHEMA; Schema: -; Owner: themis_migrator
--

CREATE SCHEMA reporting;


ALTER SCHEMA reporting OWNER TO themis_migrator;

--
-- Name: btree_gist; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA public;


--
-- Name: EXTENSION btree_gist; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION btree_gist IS 'support for indexing common datatypes in GiST';


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __ef_migrations_history; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


ALTER TABLE kernel.__ef_migrations_history OWNER TO themis_migrator;

--
-- Name: account; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_account_account_type CHECK (((account_type)::text = ANY ((ARRAY['Asset'::character varying, 'Liability'::character varying, 'Equity'::character varying, 'Revenue'::character varying, 'Expense'::character varying])::text[]))),
    CONSTRAINT ck_account_status CHECK (((status)::text = ANY ((ARRAY['Active'::character varying, 'Blocked'::character varying, 'Inactive'::character varying])::text[])))
);


ALTER TABLE kernel.account OWNER TO themis_migrator;

--
-- Name: audit_event; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.audit_event (
    id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    user_id uuid NOT NULL,
    command_name character varying(81) NOT NULL,
    entity_type character varying(40) NOT NULL,
    entity_id uuid NOT NULL,
    changes jsonb NOT NULL,
    idempotency_key character varying(128),
    correlation_id character varying(128),
    command_id uuid
);


ALTER TABLE kernel.audit_event OWNER TO themis_migrator;

--
-- Name: chart_of_accounts; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.chart_of_accounts (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    template character varying(200),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL
);


ALTER TABLE kernel.chart_of_accounts OWNER TO themis_migrator;

--
-- Name: company; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_company_fiscal_year_start_month CHECK (((fiscal_year_start_month >= 1) AND (fiscal_year_start_month <= 12))),
    CONSTRAINT ck_company_rounding_mode CHECK (((rounding_mode)::text = ANY ((ARRAY['HalfAwayFromZero'::character varying, 'HalfEven'::character varying])::text[]))),
    CONSTRAINT ck_company_status CHECK (((status)::text = ANY ((ARRAY['Active'::character varying, 'Inactive'::character varying])::text[]))),
    CONSTRAINT ck_company_tax_rounding_level CHECK (((tax_rounding_level)::text = ANY ((ARRAY['Line'::character varying, 'Document'::character varying])::text[])))
);


ALTER TABLE kernel.company OWNER TO themis_migrator;

--
-- Name: currency; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.currency (
    id uuid NOT NULL,
    code character varying(3) NOT NULL,
    name character varying(200) NOT NULL,
    minor_units integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_currency_minor_units CHECK (((minor_units >= 0) AND (minor_units <= 3)))
);


ALTER TABLE kernel.currency OWNER TO themis_migrator;

--
-- Name: document; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_document_status CHECK (((status)::text = ANY ((ARRAY['Draft'::character varying, 'Posted'::character varying, 'Discarded'::character varying, 'Reversed'::character varying])::text[])))
);


ALTER TABLE kernel.document OWNER TO themis_migrator;

--
-- Name: document_line; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_document_line_entered_unit_cost CHECK (((entered_unit_cost IS NULL) OR (entered_unit_cost >= (0)::numeric))),
    CONSTRAINT ck_document_line_item_line CHECK ((((line_type)::text <> 'Item'::text) OR ((item_id IS NOT NULL) AND (quantity IS NOT NULL) AND (unit_code IS NOT NULL) AND (quantity_base IS NOT NULL) AND (warehouse_id IS NOT NULL) AND (stock_status IS NOT NULL)))),
    CONSTRAINT ck_document_line_line_number CHECK ((line_number > 0)),
    CONSTRAINT ck_document_line_line_status CHECK (((line_status)::text = ANY ((ARRAY['Open'::character varying, 'Closed'::character varying, 'Cancelled'::character varying])::text[]))),
    CONSTRAINT ck_document_line_line_type CHECK (((line_type)::text = ANY ((ARRAY['Item'::character varying, 'Charge'::character varying, 'Account'::character varying, 'Allocation'::character varying, 'Text'::character varying])::text[]))),
    CONSTRAINT ck_document_line_quantity CHECK (((quantity IS NULL) OR (quantity <> (0)::numeric))),
    CONSTRAINT ck_document_line_stock_status CHECK (((stock_status IS NULL) OR ((stock_status)::text = ANY ((ARRAY['Available'::character varying, 'Blocked'::character varying])::text[]))))
);


ALTER TABLE kernel.document_line OWNER TO themis_migrator;

--
-- Name: document_transition; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_document_transition_from_status CHECK (((from_status)::text = ANY ((ARRAY['Draft'::character varying, 'Posted'::character varying, 'Discarded'::character varying, 'Reversed'::character varying])::text[]))),
    CONSTRAINT ck_document_transition_to_status CHECK (((to_status)::text = ANY ((ARRAY['Draft'::character varying, 'Posted'::character varying, 'Discarded'::character varying, 'Reversed'::character varying])::text[])))
);


ALTER TABLE kernel.document_transition OWNER TO themis_migrator;

--
-- Name: document_type; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_document_type_number_assigned CHECK (((number_assigned)::text = ANY ((ARRAY['OnCreate'::character varying, 'OnPost'::character varying, 'NumberingOnly'::character varying])::text[]))),
    CONSTRAINT ck_document_type_numbering_only CHECK ((((number_assigned)::text <> 'NumberingOnly'::text) OR ((posts_inventory = false) AND (posts_general_ledger = false) AND (party_role IS NULL))))
);


ALTER TABLE kernel.document_type OWNER TO themis_migrator;

--
-- Name: fiscal_period; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_fiscal_period_dates CHECK ((start_date <= end_date)),
    CONSTRAINT ck_fiscal_period_number CHECK (((period_number >= 1) AND (period_number <= 18))),
    CONSTRAINT ck_fiscal_period_status CHECK (((status)::text = ANY ((ARRAY['Open'::character varying, 'SoftClosed'::character varying, 'Closed'::character varying])::text[])))
);


ALTER TABLE kernel.fiscal_period OWNER TO themis_migrator;

--
-- Name: fiscal_year; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_fiscal_year_dates CHECK ((start_date <= end_date)),
    CONSTRAINT ck_fiscal_year_status CHECK (((status)::text = ANY ((ARRAY['Open'::character varying, 'Closed'::character varying])::text[])))
);


ALTER TABLE kernel.fiscal_year OWNER TO themis_migrator;

--
-- Name: idempotency_record; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.idempotency_record (
    user_id uuid NOT NULL,
    key character varying(128) NOT NULL,
    command_name character varying(81) NOT NULL,
    request_hash character varying(64) NOT NULL,
    response jsonb NOT NULL,
    expires_at timestamp with time zone NOT NULL
);


ALTER TABLE kernel.idempotency_record OWNER TO themis_migrator;

--
-- Name: installation; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_installation_status CHECK (((status)::text = ANY ((ARRAY['Active'::character varying, 'Suspended'::character varying])::text[])))
);


ALTER TABLE kernel.installation OWNER TO themis_migrator;

--
-- Name: inventory_adjustment; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.inventory_adjustment (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    reason_code character varying(20),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_inventory_adjustment_reason_code CHECK (((reason_code IS NULL) OR ((reason_code)::text = ANY ((ARRAY['OpeningBalance'::character varying, 'CountDifference'::character varying, 'Scrap'::character varying, 'Other'::character varying])::text[]))))
);


ALTER TABLE kernel.inventory_adjustment OWNER TO themis_migrator;

--
-- Name: inventory_entry; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_inventory_entry_cost_sign CHECK (((cost_amount = (0)::numeric) OR (sign(cost_amount) = sign(quantity_base)))),
    CONSTRAINT ck_inventory_entry_journal_entry CHECK (((cost_amount = (0)::numeric) OR (journal_entry_id IS NOT NULL))),
    CONSTRAINT ck_inventory_entry_lot_id CHECK ((lot_id IS NULL)),
    CONSTRAINT ck_inventory_entry_movement_type CHECK (((movement_type)::text = ANY ((ARRAY['AdjustmentIn'::character varying, 'AdjustmentOut'::character varying, 'Issue'::character varying, 'ReturnIn'::character varying, 'StatusOut'::character varying, 'StatusIn'::character varying])::text[]))),
    CONSTRAINT ck_inventory_entry_quantity CHECK ((quantity_base <> (0)::numeric)),
    CONSTRAINT ck_inventory_entry_stock_status CHECK (((stock_status)::text = ANY ((ARRAY['Available'::character varying, 'Blocked'::character varying])::text[])))
);


ALTER TABLE kernel.inventory_entry OWNER TO themis_migrator;

--
-- Name: item; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.item (
    id uuid NOT NULL,
    item_number character varying(40) NOT NULL,
    description character varying(200) NOT NULL,
    item_type character varying(20) NOT NULL,
    base_unit character varying(40) NOT NULL,
    lot_tracking character varying(20) NOT NULL,
    attribute_template_id uuid,
    net_weight_kg numeric(20,6),
    sales_multiple numeric(20,6),
    min_order_qty numeric(20,6),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_item_item_type CHECK (((item_type)::text = ANY ((ARRAY['Stocked'::character varying, 'NonStocked'::character varying, 'Service'::character varying])::text[]))),
    CONSTRAINT ck_item_lot_tracking CHECK (((lot_tracking)::text = ANY ((ARRAY['None'::character varying, 'Required'::character varying])::text[]))),
    CONSTRAINT ck_item_min_order_qty CHECK (((min_order_qty IS NULL) OR (min_order_qty > (0)::numeric))),
    CONSTRAINT ck_item_net_weight_kg CHECK (((net_weight_kg IS NULL) OR (net_weight_kg >= (0)::numeric))),
    CONSTRAINT ck_item_sales_multiple CHECK (((sales_multiple IS NULL) OR (sales_multiple > (0)::numeric)))
);


ALTER TABLE kernel.item OWNER TO themis_migrator;

--
-- Name: item_company; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.item_company (
    id uuid NOT NULL,
    item_id uuid NOT NULL,
    company_id uuid NOT NULL,
    status character varying(20) NOT NULL,
    item_posting_group character varying(40) NOT NULL,
    tax_class character varying(20) NOT NULL,
    costing_method character varying(20) NOT NULL,
    sales_allowed boolean NOT NULL,
    purchase_allowed boolean NOT NULL,
    default_warehouse_id uuid,
    default_sales_unit character varying(40),
    default_purchase_unit character varying(40),
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_item_company_costing_method CHECK (((costing_method)::text = 'MovingAverage'::text)),
    CONSTRAINT ck_item_company_status CHECK (((status)::text = ANY ((ARRAY['Active'::character varying, 'Blocked'::character varying, 'Inactive'::character varying])::text[]))),
    CONSTRAINT ck_item_company_tax_class CHECK (((tax_class)::text = ANY ((ARRAY['Standard'::character varying, 'Reduced'::character varying, 'Zero'::character varying, 'Exempt'::character varying])::text[])))
);


ALTER TABLE kernel.item_company OWNER TO themis_migrator;

--
-- Name: item_cost_state; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_item_cost_state_quantity CHECK ((quantity_base >= (0)::numeric)),
    CONSTRAINT ck_item_cost_state_value CHECK (((inventory_value >= (0)::numeric) AND ((quantity_base <> (0)::numeric) OR (inventory_value = (0)::numeric))))
);


ALTER TABLE kernel.item_cost_state OWNER TO themis_migrator;

--
-- Name: item_posting_group; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.item_posting_group (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL
);


ALTER TABLE kernel.item_posting_group OWNER TO themis_migrator;

--
-- Name: journal_entry; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    extension jsonb DEFAULT '{}'::jsonb NOT NULL
);


ALTER TABLE kernel.journal_entry OWNER TO themis_migrator;

--
-- Name: journal_line; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_journal_line_amount CHECK ((amount <> (0)::numeric)),
    CONSTRAINT ck_journal_line_line_number CHECK ((line_number > 0)),
    CONSTRAINT ck_journal_line_txn_sign CHECK ((sign(txn_amount) = sign(amount)))
);


ALTER TABLE kernel.journal_line OWNER TO themis_migrator;

--
-- Name: number_series; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_number_series_fiscal_year CHECK ((((scope)::text = 'PerFiscalYear'::text) = (fiscal_year_id IS NOT NULL))),
    CONSTRAINT ck_number_series_gapless CHECK (((type_requires_gapless = false) OR (gapless = true))),
    CONSTRAINT ck_number_series_journal_entry CHECK ((((document_type)::text <> 'JOURNAL_ENTRY'::text) OR ((gapless = true) AND ((scope)::text = 'PerFiscalYear'::text)))),
    CONSTRAINT ck_number_series_next_number CHECK ((next_number >= 1)),
    CONSTRAINT ck_number_series_padding CHECK (((padding >= 0) AND (padding <= 20))),
    CONSTRAINT ck_number_series_scope CHECK (((scope)::text = ANY ((ARRAY['Continuous'::character varying, 'PerFiscalYear'::character varying])::text[])))
);


ALTER TABLE kernel.number_series OWNER TO themis_migrator;

--
-- Name: outbox_event; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.outbox_event (
    id uuid NOT NULL,
    event_type character varying(81) NOT NULL,
    schema_version integer NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    published_at timestamp with time zone,
    attempts integer DEFAULT 0 NOT NULL,
    aggregate_type character varying(40) NOT NULL,
    command_name character varying(81) NOT NULL,
    company_id uuid,
    correlation_id character varying(128) NOT NULL
);


ALTER TABLE kernel.outbox_event OWNER TO themis_migrator;

--
-- Name: party; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_party_party_type CHECK (((party_type)::text = ANY ((ARRAY['Organisation'::character varying, 'Person'::character varying])::text[]))),
    CONSTRAINT ck_party_status CHECK (((status)::text = ANY ((ARRAY['Active'::character varying, 'Blocked'::character varying, 'Inactive'::character varying])::text[])))
);


ALTER TABLE kernel.party OWNER TO themis_migrator;

--
-- Name: permission; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.permission (
    id uuid NOT NULL,
    code character varying(81) NOT NULL,
    risk_level character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_permission_risk_level CHECK (((risk_level)::text = ANY ((ARRAY['Normal'::character varying, 'Sensitive'::character varying])::text[])))
);


ALTER TABLE kernel.permission OWNER TO themis_migrator;

--
-- Name: posting_profile_rule; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    expects_control boolean GENERATED ALWAYS AS (((account_role)::text = ANY ((ARRAY['INVENTORY'::character varying, 'RECEIVABLES'::character varying, 'OUTPUT_VAT'::character varying])::text[]))) STORED NOT NULL,
    valid_from date NOT NULL,
    valid_to date,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_posting_profile_rule_account_role CHECK (((account_role)::text = ANY ((ARRAY['INVENTORY'::character varying, 'COGS'::character varying, 'REVENUE'::character varying, 'RECEIVABLES'::character varying, 'OUTPUT_VAT'::character varying, 'BANK'::character varying, 'PAYMENT_DIFFERENCE'::character varying, 'REALISED_FX'::character varying, 'ROUNDING'::character varying, 'INVENTORY_ADJUSTMENT'::character varying, 'VAT_SETTLEMENT'::character varying, 'OPENING_BALANCE'::character varying])::text[]))),
    CONSTRAINT ck_posting_profile_rule_keys_by_role CHECK (((((account_role)::text = 'INVENTORY'::text) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'COGS'::text) AND (party_posting_group IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'REVENUE'::text) AND (party_posting_group IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'RECEIVABLES'::text) AND (item_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'OUTPUT_VAT'::text) AND (item_posting_group IS NULL) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'BANK'::text) AND (item_posting_group IS NULL) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'PAYMENT_DIFFERENCE'::text) AND (item_posting_group IS NULL) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'REALISED_FX'::text) AND (item_posting_group IS NULL) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'ROUNDING'::text) AND (item_posting_group IS NULL) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'INVENTORY_ADJUSTMENT'::text) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL)) OR (((account_role)::text = 'VAT_SETTLEMENT'::text) AND (item_posting_group IS NULL) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)) OR (((account_role)::text = 'OPENING_BALANCE'::text) AND (item_posting_group IS NULL) AND (party_posting_group IS NULL) AND (ship_to_scope IS NULL) AND (tax_code_id IS NULL) AND (reason_code IS NULL)))),
    CONSTRAINT ck_posting_profile_rule_party_posting_group CHECK (((party_posting_group IS NULL) OR ((party_posting_group)::text = ANY ((ARRAY['Domestic'::character varying, 'EU'::character varying, 'NonEU'::character varying])::text[])))),
    CONSTRAINT ck_posting_profile_rule_reason_code CHECK (((reason_code IS NULL) OR ((reason_code)::text = ANY ((ARRAY['OpeningBalance'::character varying, 'CountDifference'::character varying, 'Scrap'::character varying, 'Other'::character varying])::text[])))),
    CONSTRAINT ck_posting_profile_rule_ship_to_scope CHECK (((ship_to_scope IS NULL) OR ((ship_to_scope)::text = ANY ((ARRAY['Domestic'::character varying, 'EU'::character varying, 'NonEU'::character varying])::text[])))),
    CONSTRAINT ck_posting_profile_rule_validity CHECK (((valid_to IS NULL) OR (valid_to > valid_from)))
);


ALTER TABLE kernel.posting_profile_rule OWNER TO themis_migrator;

--
-- Name: role; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.role (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL
);


ALTER TABLE kernel.role OWNER TO themis_migrator;

--
-- Name: role_assignment; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.role_assignment (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    role_id uuid NOT NULL,
    company_id uuid NOT NULL,
    site_id uuid,
    valid_from date NOT NULL,
    valid_to date,
    assigned_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_role_assignment_validity CHECK (((valid_to IS NULL) OR (valid_to > valid_from)))
);


ALTER TABLE kernel.role_assignment OWNER TO themis_migrator;

--
-- Name: role_permission; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.role_permission (
    id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_code character varying(81) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL
);


ALTER TABLE kernel.role_permission OWNER TO themis_migrator;

--
-- Name: site; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL
);


ALTER TABLE kernel.site OWNER TO themis_migrator;

--
-- Name: sod_conflict; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.sod_conflict (
    id uuid NOT NULL,
    permission_a character varying(81) NOT NULL,
    permission_b character varying(81) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_sod_conflict_order CHECK (((permission_a)::text < (permission_b)::text))
);


ALTER TABLE kernel.sod_conflict OWNER TO themis_migrator;

--
-- Name: stock_balance; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_stock_balance_lot_id CHECK ((lot_id IS NULL)),
    CONSTRAINT ck_stock_balance_reserved CHECK ((reserved_base >= (0)::numeric)),
    CONSTRAINT ck_stock_balance_stock_status CHECK (((stock_status)::text = ANY ((ARRAY['Available'::character varying, 'Blocked'::character varying])::text[])))
);


ALTER TABLE kernel.stock_balance OWNER TO themis_migrator;

--
-- Name: unit_of_measure; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.unit_of_measure (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    dimension character varying(20) NOT NULL,
    decimals integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_unit_of_measure_decimals CHECK (((decimals >= 0) AND (decimals <= 6))),
    CONSTRAINT ck_unit_of_measure_dimension CHECK (((dimension)::text = ANY ((ARRAY['Count'::character varying, 'Mass'::character varying, 'Length'::character varying, 'Volume'::character varying, 'Time'::character varying])::text[])))
);


ALTER TABLE kernel.unit_of_measure OWNER TO themis_migrator;

--
-- Name: user_account; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

CREATE TABLE kernel.user_account (
    id uuid NOT NULL,
    external_subject character varying(255) NOT NULL,
    display_name character varying(200) NOT NULL,
    email character varying(320),
    is_service_account boolean NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_user_account_status CHECK (((status)::text = ANY ((ARRAY['Active'::character varying, 'Disabled'::character varying])::text[])))
);


ALTER TABLE kernel.user_account OWNER TO themis_migrator;

--
-- Name: warehouse; Type: TABLE; Schema: kernel; Owner: themis_migrator
--

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
    version bigint DEFAULT 1 NOT NULL,
    extension jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT ck_warehouse_status CHECK (((status)::text = ANY ((ARRAY['Active'::character varying, 'Blocked'::character varying, 'Inactive'::character varying])::text[])))
);


ALTER TABLE kernel.warehouse OWNER TO themis_migrator;

--
-- Name: stock_by_warehouse; Type: VIEW; Schema: reporting; Owner: themis_migrator
--

CREATE VIEW reporting.stock_by_warehouse AS
 WITH balance AS (
         SELECT b.company_id,
            b.item_id,
            b.warehouse_id,
            sum(b.on_hand_base) AS on_hand_total,
            COALESCE(sum(b.on_hand_base) FILTER (WHERE ((b.stock_status)::text = 'Available'::text)), (0)::numeric) AS on_hand_available,
            COALESCE(sum(b.on_hand_base) FILTER (WHERE ((b.stock_status)::text = 'Blocked'::text)), (0)::numeric) AS on_hand_blocked,
            sum(b.reserved_base) AS reserved
           FROM kernel.stock_balance b
          GROUP BY b.company_id, b.item_id, b.warehouse_id
         HAVING (sum(b.on_hand_base) <> (0)::numeric)
        ), share AS (
         SELECT bal.company_id,
            bal.item_id,
            bal.warehouse_id,
            bal.on_hand_total,
            bal.on_hand_available,
            bal.on_hand_blocked,
            bal.reserved,
            s.inventory_value,
            s.average_cost,
            trunc(((s.inventory_value * bal.on_hand_total) / NULLIF(s.quantity_base, (0)::numeric)), cur.minor_units) AS share_value,
            row_number() OVER (PARTITION BY bal.company_id, bal.item_id ORDER BY bal.on_hand_total DESC, bal.warehouse_id) AS allocation_rank
           FROM (((balance bal
             JOIN kernel.item_cost_state s ON (((s.company_id = bal.company_id) AND (s.item_id = bal.item_id))))
             JOIN kernel.company c_1 ON ((c_1.id = bal.company_id)))
             JOIN kernel.currency cur ON (((cur.code)::text = (c_1.base_currency)::text)))
        ), allocated AS (
         SELECT sh.company_id,
            sh.item_id,
            sh.warehouse_id,
            sh.on_hand_total,
            sh.on_hand_available,
            sh.on_hand_blocked,
            sh.reserved,
            sh.inventory_value,
            sh.average_cost,
            sh.share_value,
            sh.allocation_rank,
            (sh.share_value +
                CASE
                    WHEN (sh.allocation_rank = 1) THEN (sh.inventory_value - sum(sh.share_value) OVER (PARTITION BY sh.company_id, sh.item_id))
                    ELSE (0)::numeric
                END) AS stock_value
           FROM share sh
        )
 SELECT a.company_id,
    c.code AS company_code,
    a.item_id,
    i.item_number,
    i.description AS item_description,
    a.warehouse_id,
    w.code AS warehouse_code,
    i.base_unit AS unit_code,
    a.on_hand_total,
    a.on_hand_available,
    a.on_hand_blocked,
    a.reserved,
    a.stock_value,
    c.base_currency AS currency_code,
    a.average_cost AS item_average_cost
   FROM (((allocated a
     JOIN kernel.company c ON ((c.id = a.company_id)))
     JOIN kernel.item i ON ((i.id = a.item_id)))
     JOIN kernel.warehouse w ON ((w.id = a.warehouse_id)));


ALTER VIEW reporting.stock_by_warehouse OWNER TO themis_migrator;

--
-- Name: VIEW stock_by_warehouse; Type: COMMENT; Schema: reporting; Owner: themis_migrator
--

COMMENT ON VIEW reporting.stock_by_warehouse IS 'Stock value KPI, docs/kpi/stock-value.md version 1 (D-54, I-15): current stock quantity and value per company, item and warehouse from the caches; value allocated by share of on hand, rounded down to the minor unit, remainder to the warehouse with the largest on hand.';


--
-- Name: warehouse AK_warehouse_id_company_id; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.warehouse
    ADD CONSTRAINT "AK_warehouse_id_company_id" UNIQUE (id, company_id);


--
-- Name: __ef_migrations_history PK___ef_migrations_history; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.__ef_migrations_history
    ADD CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId");


--
-- Name: account ak_account_id_is_control; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.account
    ADD CONSTRAINT ak_account_id_is_control UNIQUE (id, is_control);


--
-- Name: currency ak_currency_code; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.currency
    ADD CONSTRAINT ak_currency_code UNIQUE (code);


--
-- Name: document ak_document_id_company_id; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document
    ADD CONSTRAINT ak_document_id_company_id UNIQUE (id, company_id);


--
-- Name: document_line ak_document_line_id_company_id; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_line
    ADD CONSTRAINT ak_document_line_id_company_id UNIQUE (id, company_id);


--
-- Name: document_type ak_document_type_code; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_type
    ADD CONSTRAINT ak_document_type_code UNIQUE (code);


--
-- Name: document_type ak_document_type_code_gapless; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_type
    ADD CONSTRAINT ak_document_type_code_gapless UNIQUE (code, gapless);


--
-- Name: fiscal_year ak_fiscal_year_id_company_id; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.fiscal_year
    ADD CONSTRAINT ak_fiscal_year_id_company_id UNIQUE (id, company_id);


--
-- Name: item_company ak_item_company_item_company; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_company
    ADD CONSTRAINT ak_item_company_item_company UNIQUE (item_id, company_id);


--
-- Name: item_posting_group ak_item_posting_group_code; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_posting_group
    ADD CONSTRAINT ak_item_posting_group_code UNIQUE (code);


--
-- Name: journal_entry ak_journal_entry_id_company_id; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_entry
    ADD CONSTRAINT ak_journal_entry_id_company_id UNIQUE (id, company_id);


--
-- Name: permission ak_permission_code; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.permission
    ADD CONSTRAINT ak_permission_code UNIQUE (code);


--
-- Name: site ak_site_id_company_id; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.site
    ADD CONSTRAINT ak_site_id_company_id UNIQUE (id, company_id);


--
-- Name: unit_of_measure ak_unit_of_measure_code; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.unit_of_measure
    ADD CONSTRAINT ak_unit_of_measure_code UNIQUE (code);


--
-- Name: audit_event ck_audit_event_command_id; Type: CHECK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE kernel.audit_event
    ADD CONSTRAINT ck_audit_event_command_id CHECK ((command_id IS NOT NULL)) NOT VALID;


--
-- Name: posting_profile_rule ex_posting_profile_rule_no_overlap; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.posting_profile_rule
    ADD CONSTRAINT ex_posting_profile_rule_no_overlap EXCLUDE USING gist (company_id WITH =, account_role WITH =, COALESCE(item_posting_group, ''::character varying) WITH =, COALESCE(party_posting_group, ''::character varying) WITH =, COALESCE(ship_to_scope, ''::character varying) WITH =, COALESCE(tax_code_id, '00000000-0000-0000-0000-000000000000'::uuid) WITH =, COALESCE(reason_code, ''::character varying) WITH =, daterange(valid_from, valid_to, '[)'::text) WITH &&);


--
-- Name: account pk_account; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.account
    ADD CONSTRAINT pk_account PRIMARY KEY (id);


--
-- Name: audit_event pk_audit_event; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.audit_event
    ADD CONSTRAINT pk_audit_event PRIMARY KEY (id);


--
-- Name: chart_of_accounts pk_chart_of_accounts; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.chart_of_accounts
    ADD CONSTRAINT pk_chart_of_accounts PRIMARY KEY (id);


--
-- Name: company pk_company; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.company
    ADD CONSTRAINT pk_company PRIMARY KEY (id);


--
-- Name: currency pk_currency; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.currency
    ADD CONSTRAINT pk_currency PRIMARY KEY (id);


--
-- Name: document pk_document; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document
    ADD CONSTRAINT pk_document PRIMARY KEY (id);


--
-- Name: document_line pk_document_line; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_line
    ADD CONSTRAINT pk_document_line PRIMARY KEY (id);


--
-- Name: document_transition pk_document_transition; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_transition
    ADD CONSTRAINT pk_document_transition PRIMARY KEY (id);


--
-- Name: document_type pk_document_type; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_type
    ADD CONSTRAINT pk_document_type PRIMARY KEY (id);


--
-- Name: fiscal_period pk_fiscal_period; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.fiscal_period
    ADD CONSTRAINT pk_fiscal_period PRIMARY KEY (id);


--
-- Name: fiscal_year pk_fiscal_year; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.fiscal_year
    ADD CONSTRAINT pk_fiscal_year PRIMARY KEY (id);


--
-- Name: idempotency_record pk_idempotency_record; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.idempotency_record
    ADD CONSTRAINT pk_idempotency_record PRIMARY KEY (user_id, key);


--
-- Name: installation pk_installation; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.installation
    ADD CONSTRAINT pk_installation PRIMARY KEY (id);


--
-- Name: inventory_adjustment pk_inventory_adjustment; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_adjustment
    ADD CONSTRAINT pk_inventory_adjustment PRIMARY KEY (id);


--
-- Name: inventory_entry pk_inventory_entry; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_entry
    ADD CONSTRAINT pk_inventory_entry PRIMARY KEY (id);


--
-- Name: item pk_item; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item
    ADD CONSTRAINT pk_item PRIMARY KEY (id);


--
-- Name: item_company pk_item_company; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_company
    ADD CONSTRAINT pk_item_company PRIMARY KEY (id);


--
-- Name: item_cost_state pk_item_cost_state; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_cost_state
    ADD CONSTRAINT pk_item_cost_state PRIMARY KEY (id);


--
-- Name: item_posting_group pk_item_posting_group; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_posting_group
    ADD CONSTRAINT pk_item_posting_group PRIMARY KEY (id);


--
-- Name: journal_entry pk_journal_entry; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_entry
    ADD CONSTRAINT pk_journal_entry PRIMARY KEY (id);


--
-- Name: journal_line pk_journal_line; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT pk_journal_line PRIMARY KEY (id);


--
-- Name: number_series pk_number_series; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.number_series
    ADD CONSTRAINT pk_number_series PRIMARY KEY (id);


--
-- Name: outbox_event pk_outbox_event; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.outbox_event
    ADD CONSTRAINT pk_outbox_event PRIMARY KEY (id);


--
-- Name: party pk_party; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.party
    ADD CONSTRAINT pk_party PRIMARY KEY (id);


--
-- Name: permission pk_permission; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.permission
    ADD CONSTRAINT pk_permission PRIMARY KEY (id);


--
-- Name: posting_profile_rule pk_posting_profile_rule; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.posting_profile_rule
    ADD CONSTRAINT pk_posting_profile_rule PRIMARY KEY (id);


--
-- Name: role pk_role; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role
    ADD CONSTRAINT pk_role PRIMARY KEY (id);


--
-- Name: role_assignment pk_role_assignment; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_assignment
    ADD CONSTRAINT pk_role_assignment PRIMARY KEY (id);


--
-- Name: role_permission pk_role_permission; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_permission
    ADD CONSTRAINT pk_role_permission PRIMARY KEY (id);


--
-- Name: site pk_site; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.site
    ADD CONSTRAINT pk_site PRIMARY KEY (id);


--
-- Name: sod_conflict pk_sod_conflict; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.sod_conflict
    ADD CONSTRAINT pk_sod_conflict PRIMARY KEY (id);


--
-- Name: stock_balance pk_stock_balance; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.stock_balance
    ADD CONSTRAINT pk_stock_balance PRIMARY KEY (id);


--
-- Name: unit_of_measure pk_unit_of_measure; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.unit_of_measure
    ADD CONSTRAINT pk_unit_of_measure PRIMARY KEY (id);


--
-- Name: user_account pk_user_account; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.user_account
    ADD CONSTRAINT pk_user_account PRIMARY KEY (id);


--
-- Name: warehouse pk_warehouse; Type: CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.warehouse
    ADD CONSTRAINT pk_warehouse PRIMARY KEY (id);


--
-- Name: IX_audit_event_user_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_audit_event_user_id" ON kernel.audit_event USING btree (user_id);


--
-- Name: IX_company_base_currency; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_company_base_currency" ON kernel.company USING btree (base_currency);


--
-- Name: IX_company_chart_of_accounts_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_company_chart_of_accounts_id" ON kernel.company USING btree (chart_of_accounts_id);


--
-- Name: IX_company_party_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_company_party_id" ON kernel.company USING btree (party_id);


--
-- Name: IX_document_currency; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_document_currency" ON kernel.document USING btree (currency);


--
-- Name: IX_document_document_type; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_document_document_type" ON kernel.document USING btree (document_type);


--
-- Name: IX_document_line_document_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_document_line_document_id_company_id" ON kernel.document_line USING btree (document_id, company_id);


--
-- Name: IX_document_line_item_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_document_line_item_id_company_id" ON kernel.document_line USING btree (item_id, company_id);


--
-- Name: IX_document_line_unit_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_document_line_unit_code" ON kernel.document_line USING btree (unit_code);


--
-- Name: IX_document_line_warehouse_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_document_line_warehouse_id_company_id" ON kernel.document_line USING btree (warehouse_id, company_id);


--
-- Name: IX_document_warehouse_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_document_warehouse_id_company_id" ON kernel.document USING btree (warehouse_id, company_id);


--
-- Name: IX_fiscal_period_fiscal_year_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_fiscal_period_fiscal_year_id_company_id" ON kernel.fiscal_period USING btree (fiscal_year_id, company_id);


--
-- Name: IX_inventory_adjustment_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX "IX_inventory_adjustment_id_company_id" ON kernel.inventory_adjustment USING btree (id, company_id);


--
-- Name: IX_inventory_entry_item_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_inventory_entry_item_id_company_id" ON kernel.inventory_entry USING btree (item_id, company_id);


--
-- Name: IX_inventory_entry_journal_entry_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_inventory_entry_journal_entry_id_company_id" ON kernel.inventory_entry USING btree (journal_entry_id, company_id);


--
-- Name: IX_inventory_entry_reversal_of_entry_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_inventory_entry_reversal_of_entry_id" ON kernel.inventory_entry USING btree (reversal_of_entry_id);


--
-- Name: IX_inventory_entry_source_line_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_inventory_entry_source_line_id_company_id" ON kernel.inventory_entry USING btree (source_line_id, company_id);


--
-- Name: IX_inventory_entry_warehouse_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_inventory_entry_warehouse_id_company_id" ON kernel.inventory_entry USING btree (warehouse_id, company_id);


--
-- Name: IX_item_base_unit; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_item_base_unit" ON kernel.item USING btree (base_unit);


--
-- Name: IX_item_company_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_item_company_company_id" ON kernel.item_company USING btree (company_id);


--
-- Name: IX_item_company_default_warehouse_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_item_company_default_warehouse_id_company_id" ON kernel.item_company USING btree (default_warehouse_id, company_id);


--
-- Name: IX_item_company_item_posting_group; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_item_company_item_posting_group" ON kernel.item_company USING btree (item_posting_group);


--
-- Name: IX_item_cost_state_item_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_item_cost_state_item_id_company_id" ON kernel.item_cost_state USING btree (item_id, company_id);


--
-- Name: IX_item_cost_state_last_inventory_entry_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_item_cost_state_last_inventory_entry_id" ON kernel.item_cost_state USING btree (last_inventory_entry_id);


--
-- Name: IX_journal_entry_fiscal_period_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_entry_fiscal_period_id" ON kernel.journal_entry USING btree (fiscal_period_id);


--
-- Name: IX_journal_entry_reversal_of_entry_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_entry_reversal_of_entry_id" ON kernel.journal_entry USING btree (reversal_of_entry_id);


--
-- Name: IX_journal_entry_source_document_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_entry_source_document_id_company_id" ON kernel.journal_entry USING btree (source_document_id, company_id);


--
-- Name: IX_journal_line_item_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_line_item_id_company_id" ON kernel.journal_line USING btree (item_id, company_id);


--
-- Name: IX_journal_line_journal_entry_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_line_journal_entry_id_company_id" ON kernel.journal_line USING btree (journal_entry_id, company_id);


--
-- Name: IX_journal_line_posting_rule_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_line_posting_rule_id" ON kernel.journal_line USING btree (posting_rule_id);


--
-- Name: IX_journal_line_source_line_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_line_source_line_id_company_id" ON kernel.journal_line USING btree (source_line_id, company_id);


--
-- Name: IX_journal_line_txn_currency; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_line_txn_currency" ON kernel.journal_line USING btree (txn_currency);


--
-- Name: IX_journal_line_warehouse_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_journal_line_warehouse_id_company_id" ON kernel.journal_line USING btree (warehouse_id, company_id);


--
-- Name: IX_number_series_document_type_type_requires_gapless; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_number_series_document_type_type_requires_gapless" ON kernel.number_series USING btree (document_type, type_requires_gapless);


--
-- Name: IX_number_series_fiscal_year_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_number_series_fiscal_year_id_company_id" ON kernel.number_series USING btree (fiscal_year_id, company_id);


--
-- Name: IX_outbox_event_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_outbox_event_company_id" ON kernel.outbox_event USING btree (company_id);


--
-- Name: IX_posting_profile_rule_account_id_expects_control; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_posting_profile_rule_account_id_expects_control" ON kernel.posting_profile_rule USING btree (account_id, expects_control);


--
-- Name: IX_posting_profile_rule_item_posting_group; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_posting_profile_rule_item_posting_group" ON kernel.posting_profile_rule USING btree (item_posting_group);


--
-- Name: IX_role_assignment_assigned_by; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_role_assignment_assigned_by" ON kernel.role_assignment USING btree (assigned_by);


--
-- Name: IX_role_assignment_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_role_assignment_company_id" ON kernel.role_assignment USING btree (company_id);


--
-- Name: IX_role_assignment_role_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_role_assignment_role_id" ON kernel.role_assignment USING btree (role_id);


--
-- Name: IX_role_assignment_site_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_role_assignment_site_id_company_id" ON kernel.role_assignment USING btree (site_id, company_id);


--
-- Name: IX_role_permission_permission_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_role_permission_permission_code" ON kernel.role_permission USING btree (permission_code);


--
-- Name: IX_stock_balance_item_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_stock_balance_item_id_company_id" ON kernel.stock_balance USING btree (item_id, company_id);


--
-- Name: IX_stock_balance_last_inventory_entry_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_stock_balance_last_inventory_entry_id" ON kernel.stock_balance USING btree (last_inventory_entry_id);


--
-- Name: IX_stock_balance_warehouse_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_stock_balance_warehouse_id_company_id" ON kernel.stock_balance USING btree (warehouse_id, company_id);


--
-- Name: IX_warehouse_site_id_company_id; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX "IX_warehouse_site_id_company_id" ON kernel.warehouse USING btree (site_id, company_id);


--
-- Name: ix_audit_event_entity; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_audit_event_entity ON kernel.audit_event USING btree (entity_type, entity_id);


--
-- Name: ix_document_company_type_status; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_document_company_type_status ON kernel.document USING btree (company_id, document_type, status);


--
-- Name: ix_fiscal_period_company_start; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_fiscal_period_company_start ON kernel.fiscal_period USING btree (company_id, start_date);


--
-- Name: ix_idempotency_record_expires_at; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_idempotency_record_expires_at ON kernel.idempotency_record USING btree (expires_at);


--
-- Name: ix_inventory_entry_balance_key; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_inventory_entry_balance_key ON kernel.inventory_entry USING btree (company_id, item_id, warehouse_id, stock_status);


--
-- Name: ix_inventory_entry_journal_entry; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_inventory_entry_journal_entry ON kernel.inventory_entry USING btree (journal_entry_id);


--
-- Name: ix_journal_entry_company_posting_date; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_journal_entry_company_posting_date ON kernel.journal_entry USING btree (company_id, posting_date);


--
-- Name: ix_journal_line_account; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_journal_line_account ON kernel.journal_line USING btree (account_id);


--
-- Name: ix_outbox_event_unpublished; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_outbox_event_unpublished ON kernel.outbox_event USING btree (occurred_at) WHERE (published_at IS NULL);


--
-- Name: ix_party_duplicate_key; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_party_duplicate_key ON kernel.party USING btree (duplicate_key);


--
-- Name: ix_posting_profile_rule_company_role; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_posting_profile_rule_company_role ON kernel.posting_profile_rule USING btree (company_id, account_role);


--
-- Name: ix_role_assignment_user_company; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE INDEX ix_role_assignment_user_company ON kernel.role_assignment USING btree (user_id, company_id);


--
-- Name: ux_account_chart_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_account_chart_number ON kernel.account USING btree (chart_id, account_number);


--
-- Name: ux_chart_of_accounts_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_chart_of_accounts_code ON kernel.chart_of_accounts USING btree (code);


--
-- Name: ux_company_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_company_code ON kernel.company USING btree (code);


--
-- Name: ux_document_company_type_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_document_company_type_number ON kernel.document USING btree (company_id, document_type, document_number) WHERE (document_number IS NOT NULL);


--
-- Name: ux_document_line_document_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_document_line_document_number ON kernel.document_line USING btree (document_id, line_number);


--
-- Name: ux_document_transition_type_command_from; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_document_transition_type_command_from ON kernel.document_transition USING btree (document_type, command, from_status);


--
-- Name: ux_fiscal_period_year_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_fiscal_period_year_number ON kernel.fiscal_period USING btree (fiscal_year_id, period_number);


--
-- Name: ux_fiscal_year_company_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_fiscal_year_company_code ON kernel.fiscal_year USING btree (company_id, code);


--
-- Name: ux_installation_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_installation_code ON kernel.installation USING btree (code);


--
-- Name: ux_item_cost_state_company_item; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_item_cost_state_company_item ON kernel.item_cost_state USING btree (company_id, item_id);


--
-- Name: ux_item_item_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_item_item_number ON kernel.item USING btree (item_number);


--
-- Name: ux_journal_entry_company_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_journal_entry_company_number ON kernel.journal_entry USING btree (company_id, entry_number);


--
-- Name: ux_journal_line_entry_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_journal_line_entry_number ON kernel.journal_line USING btree (journal_entry_id, line_number);


--
-- Name: ux_number_series_company_type_year; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_number_series_company_type_year ON kernel.number_series USING btree (company_id, document_type, fiscal_year_id) NULLS NOT DISTINCT;


--
-- Name: ux_party_party_number; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_party_party_number ON kernel.party USING btree (party_number);


--
-- Name: ux_permission_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_permission_code ON kernel.permission USING btree (code);


--
-- Name: ux_role_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_role_code ON kernel.role USING btree (code);


--
-- Name: ux_role_permission_role_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_role_permission_role_code ON kernel.role_permission USING btree (role_id, permission_code);


--
-- Name: ux_site_company_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_site_company_code ON kernel.site USING btree (company_id, code);


--
-- Name: ux_sod_conflict_pair; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_sod_conflict_pair ON kernel.sod_conflict USING btree (permission_a, permission_b);


--
-- Name: ux_stock_balance_key; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_stock_balance_key ON kernel.stock_balance USING btree (company_id, item_id, warehouse_id, lot_id, stock_status) NULLS NOT DISTINCT;


--
-- Name: ux_user_account_external_subject; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_user_account_external_subject ON kernel.user_account USING btree (external_subject);


--
-- Name: ux_warehouse_company_code; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_warehouse_company_code ON kernel.warehouse USING btree (company_id, code);


--
-- Name: ux_warehouse_site_default; Type: INDEX; Schema: kernel; Owner: themis_migrator
--

CREATE UNIQUE INDEX ux_warehouse_site_default ON kernel.warehouse USING btree (site_id) WHERE is_default;


--
-- Name: account fk_account_chart; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.account
    ADD CONSTRAINT fk_account_chart FOREIGN KEY (chart_id) REFERENCES kernel.chart_of_accounts(id) ON DELETE RESTRICT;


--
-- Name: audit_event fk_audit_event_user; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.audit_event
    ADD CONSTRAINT fk_audit_event_user FOREIGN KEY (user_id) REFERENCES kernel.user_account(id) ON DELETE RESTRICT;


--
-- Name: company fk_company_base_currency; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.company
    ADD CONSTRAINT fk_company_base_currency FOREIGN KEY (base_currency) REFERENCES kernel.currency(code) ON DELETE RESTRICT;


--
-- Name: company fk_company_chart_of_accounts; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.company
    ADD CONSTRAINT fk_company_chart_of_accounts FOREIGN KEY (chart_of_accounts_id) REFERENCES kernel.chart_of_accounts(id) ON DELETE RESTRICT;


--
-- Name: company fk_company_party; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.company
    ADD CONSTRAINT fk_company_party FOREIGN KEY (party_id) REFERENCES kernel.party(id) ON DELETE RESTRICT;


--
-- Name: document fk_document_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document
    ADD CONSTRAINT fk_document_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: document fk_document_currency; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document
    ADD CONSTRAINT fk_document_currency FOREIGN KEY (currency) REFERENCES kernel.currency(code) ON DELETE RESTRICT;


--
-- Name: document_line fk_document_line_document_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_line
    ADD CONSTRAINT fk_document_line_document_company FOREIGN KEY (document_id, company_id) REFERENCES kernel.document(id, company_id) ON DELETE RESTRICT;


--
-- Name: document_line fk_document_line_item_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_line
    ADD CONSTRAINT fk_document_line_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company(item_id, company_id) ON DELETE RESTRICT;


--
-- Name: document_line fk_document_line_unit; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_line
    ADD CONSTRAINT fk_document_line_unit FOREIGN KEY (unit_code) REFERENCES kernel.unit_of_measure(code) ON DELETE RESTRICT;


--
-- Name: document_line fk_document_line_warehouse_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_line
    ADD CONSTRAINT fk_document_line_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse(id, company_id) ON DELETE RESTRICT;


--
-- Name: document_transition fk_document_transition_type; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document_transition
    ADD CONSTRAINT fk_document_transition_type FOREIGN KEY (document_type) REFERENCES kernel.document_type(code) ON DELETE RESTRICT;


--
-- Name: document fk_document_type; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document
    ADD CONSTRAINT fk_document_type FOREIGN KEY (document_type) REFERENCES kernel.document_type(code) ON DELETE RESTRICT;


--
-- Name: document fk_document_warehouse_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.document
    ADD CONSTRAINT fk_document_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse(id, company_id) ON DELETE RESTRICT;


--
-- Name: fiscal_period fk_fiscal_period_year_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.fiscal_period
    ADD CONSTRAINT fk_fiscal_period_year_company FOREIGN KEY (fiscal_year_id, company_id) REFERENCES kernel.fiscal_year(id, company_id) ON DELETE RESTRICT;


--
-- Name: fiscal_year fk_fiscal_year_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.fiscal_year
    ADD CONSTRAINT fk_fiscal_year_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: idempotency_record fk_idempotency_record_user; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.idempotency_record
    ADD CONSTRAINT fk_idempotency_record_user FOREIGN KEY (user_id) REFERENCES kernel.user_account(id) ON DELETE RESTRICT;


--
-- Name: inventory_adjustment fk_inventory_adjustment_document_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_adjustment
    ADD CONSTRAINT fk_inventory_adjustment_document_company FOREIGN KEY (id, company_id) REFERENCES kernel.document(id, company_id) ON DELETE RESTRICT;


--
-- Name: inventory_entry fk_inventory_entry_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_entry
    ADD CONSTRAINT fk_inventory_entry_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: inventory_entry fk_inventory_entry_item_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_entry
    ADD CONSTRAINT fk_inventory_entry_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company(item_id, company_id) ON DELETE RESTRICT;


--
-- Name: inventory_entry fk_inventory_entry_journal_entry_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_entry
    ADD CONSTRAINT fk_inventory_entry_journal_entry_company FOREIGN KEY (journal_entry_id, company_id) REFERENCES kernel.journal_entry(id, company_id) ON DELETE RESTRICT;


--
-- Name: inventory_entry fk_inventory_entry_reversal_of; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_entry
    ADD CONSTRAINT fk_inventory_entry_reversal_of FOREIGN KEY (reversal_of_entry_id) REFERENCES kernel.inventory_entry(id) ON DELETE RESTRICT;


--
-- Name: inventory_entry fk_inventory_entry_source_line_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_entry
    ADD CONSTRAINT fk_inventory_entry_source_line_company FOREIGN KEY (source_line_id, company_id) REFERENCES kernel.document_line(id, company_id) ON DELETE RESTRICT;


--
-- Name: inventory_entry fk_inventory_entry_warehouse_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.inventory_entry
    ADD CONSTRAINT fk_inventory_entry_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse(id, company_id) ON DELETE RESTRICT;


--
-- Name: item fk_item_base_unit; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item
    ADD CONSTRAINT fk_item_base_unit FOREIGN KEY (base_unit) REFERENCES kernel.unit_of_measure(code) ON DELETE RESTRICT;


--
-- Name: item_company fk_item_company_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_company
    ADD CONSTRAINT fk_item_company_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: item_company fk_item_company_default_warehouse_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_company
    ADD CONSTRAINT fk_item_company_default_warehouse_company FOREIGN KEY (default_warehouse_id, company_id) REFERENCES kernel.warehouse(id, company_id) ON DELETE RESTRICT;


--
-- Name: item_company fk_item_company_item; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_company
    ADD CONSTRAINT fk_item_company_item FOREIGN KEY (item_id) REFERENCES kernel.item(id) ON DELETE RESTRICT;


--
-- Name: item_company fk_item_company_item_posting_group; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_company
    ADD CONSTRAINT fk_item_company_item_posting_group FOREIGN KEY (item_posting_group) REFERENCES kernel.item_posting_group(code) ON DELETE RESTRICT;


--
-- Name: item_cost_state fk_item_cost_state_item_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_cost_state
    ADD CONSTRAINT fk_item_cost_state_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company(item_id, company_id) ON DELETE RESTRICT;


--
-- Name: item_cost_state fk_item_cost_state_last_entry; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.item_cost_state
    ADD CONSTRAINT fk_item_cost_state_last_entry FOREIGN KEY (last_inventory_entry_id) REFERENCES kernel.inventory_entry(id) ON DELETE RESTRICT;


--
-- Name: journal_entry fk_journal_entry_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_entry
    ADD CONSTRAINT fk_journal_entry_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: journal_entry fk_journal_entry_fiscal_period; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_entry
    ADD CONSTRAINT fk_journal_entry_fiscal_period FOREIGN KEY (fiscal_period_id) REFERENCES kernel.fiscal_period(id) ON DELETE RESTRICT;


--
-- Name: journal_entry fk_journal_entry_reversal_of; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_entry
    ADD CONSTRAINT fk_journal_entry_reversal_of FOREIGN KEY (reversal_of_entry_id) REFERENCES kernel.journal_entry(id) ON DELETE RESTRICT;


--
-- Name: journal_entry fk_journal_entry_source_document_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_entry
    ADD CONSTRAINT fk_journal_entry_source_document_company FOREIGN KEY (source_document_id, company_id) REFERENCES kernel.document(id, company_id) ON DELETE RESTRICT;


--
-- Name: journal_line fk_journal_line_account; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT fk_journal_line_account FOREIGN KEY (account_id) REFERENCES kernel.account(id) ON DELETE RESTRICT;


--
-- Name: journal_line fk_journal_line_entry_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT fk_journal_line_entry_company FOREIGN KEY (journal_entry_id, company_id) REFERENCES kernel.journal_entry(id, company_id) ON DELETE RESTRICT;


--
-- Name: journal_line fk_journal_line_item_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT fk_journal_line_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company(item_id, company_id) ON DELETE RESTRICT;


--
-- Name: journal_line fk_journal_line_posting_rule; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT fk_journal_line_posting_rule FOREIGN KEY (posting_rule_id) REFERENCES kernel.posting_profile_rule(id) ON DELETE RESTRICT;


--
-- Name: journal_line fk_journal_line_source_line_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT fk_journal_line_source_line_company FOREIGN KEY (source_line_id, company_id) REFERENCES kernel.document_line(id, company_id) ON DELETE RESTRICT;


--
-- Name: journal_line fk_journal_line_txn_currency; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT fk_journal_line_txn_currency FOREIGN KEY (txn_currency) REFERENCES kernel.currency(code) ON DELETE RESTRICT;


--
-- Name: journal_line fk_journal_line_warehouse_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.journal_line
    ADD CONSTRAINT fk_journal_line_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse(id, company_id) ON DELETE RESTRICT;


--
-- Name: number_series fk_number_series_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.number_series
    ADD CONSTRAINT fk_number_series_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: number_series fk_number_series_fiscal_year_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.number_series
    ADD CONSTRAINT fk_number_series_fiscal_year_company FOREIGN KEY (fiscal_year_id, company_id) REFERENCES kernel.fiscal_year(id, company_id) ON DELETE RESTRICT;


--
-- Name: number_series fk_number_series_type_gapless; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.number_series
    ADD CONSTRAINT fk_number_series_type_gapless FOREIGN KEY (document_type, type_requires_gapless) REFERENCES kernel.document_type(code, gapless) ON DELETE RESTRICT;


--
-- Name: outbox_event fk_outbox_event_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.outbox_event
    ADD CONSTRAINT fk_outbox_event_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: posting_profile_rule fk_posting_profile_rule_account_control; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.posting_profile_rule
    ADD CONSTRAINT fk_posting_profile_rule_account_control FOREIGN KEY (account_id, expects_control) REFERENCES kernel.account(id, is_control) ON DELETE RESTRICT;


--
-- Name: posting_profile_rule fk_posting_profile_rule_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.posting_profile_rule
    ADD CONSTRAINT fk_posting_profile_rule_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: posting_profile_rule fk_posting_profile_rule_item_posting_group; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.posting_profile_rule
    ADD CONSTRAINT fk_posting_profile_rule_item_posting_group FOREIGN KEY (item_posting_group) REFERENCES kernel.item_posting_group(code) ON DELETE RESTRICT;


--
-- Name: role_assignment fk_role_assignment_assigned_by; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_assignment
    ADD CONSTRAINT fk_role_assignment_assigned_by FOREIGN KEY (assigned_by) REFERENCES kernel.user_account(id) ON DELETE RESTRICT;


--
-- Name: role_assignment fk_role_assignment_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_assignment
    ADD CONSTRAINT fk_role_assignment_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: role_assignment fk_role_assignment_role; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_assignment
    ADD CONSTRAINT fk_role_assignment_role FOREIGN KEY (role_id) REFERENCES kernel.role(id) ON DELETE RESTRICT;


--
-- Name: role_assignment fk_role_assignment_site_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_assignment
    ADD CONSTRAINT fk_role_assignment_site_company FOREIGN KEY (site_id, company_id) REFERENCES kernel.site(id, company_id) ON DELETE RESTRICT;


--
-- Name: role_assignment fk_role_assignment_user; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_assignment
    ADD CONSTRAINT fk_role_assignment_user FOREIGN KEY (user_id) REFERENCES kernel.user_account(id) ON DELETE RESTRICT;


--
-- Name: role_permission fk_role_permission_permission; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_permission
    ADD CONSTRAINT fk_role_permission_permission FOREIGN KEY (permission_code) REFERENCES kernel.permission(code) ON DELETE RESTRICT;


--
-- Name: role_permission fk_role_permission_role; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.role_permission
    ADD CONSTRAINT fk_role_permission_role FOREIGN KEY (role_id) REFERENCES kernel.role(id) ON DELETE RESTRICT;


--
-- Name: site fk_site_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.site
    ADD CONSTRAINT fk_site_company FOREIGN KEY (company_id) REFERENCES kernel.company(id) ON DELETE RESTRICT;


--
-- Name: stock_balance fk_stock_balance_item_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.stock_balance
    ADD CONSTRAINT fk_stock_balance_item_company FOREIGN KEY (item_id, company_id) REFERENCES kernel.item_company(item_id, company_id) ON DELETE RESTRICT;


--
-- Name: stock_balance fk_stock_balance_last_entry; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.stock_balance
    ADD CONSTRAINT fk_stock_balance_last_entry FOREIGN KEY (last_inventory_entry_id) REFERENCES kernel.inventory_entry(id) ON DELETE RESTRICT;


--
-- Name: stock_balance fk_stock_balance_warehouse_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.stock_balance
    ADD CONSTRAINT fk_stock_balance_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse(id, company_id) ON DELETE RESTRICT;


--
-- Name: warehouse fk_warehouse_site_company; Type: FK CONSTRAINT; Schema: kernel; Owner: themis_migrator
--

ALTER TABLE ONLY kernel.warehouse
    ADD CONSTRAINT fk_warehouse_site_company FOREIGN KEY (site_id, company_id) REFERENCES kernel.site(id, company_id) ON DELETE RESTRICT;


--
-- Name: SCHEMA kernel; Type: ACL; Schema: -; Owner: themis_migrator
--

GRANT USAGE ON SCHEMA kernel TO themis_kernel;


--
-- Name: SCHEMA reporting; Type: ACL; Schema: -; Owner: themis_migrator
--

GRANT USAGE ON SCHEMA reporting TO themis_reporting;


--
-- Name: TABLE account; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.account TO themis_kernel;


--
-- Name: TABLE audit_event; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT ON TABLE kernel.audit_event TO themis_kernel;


--
-- Name: TABLE chart_of_accounts; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.chart_of_accounts TO themis_kernel;


--
-- Name: TABLE company; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.company TO themis_kernel;


--
-- Name: TABLE currency; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.currency TO themis_kernel;


--
-- Name: TABLE document; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.document TO themis_kernel;


--
-- Name: TABLE document_line; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.document_line TO themis_kernel;


--
-- Name: TABLE document_transition; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT ON TABLE kernel.document_transition TO themis_kernel;


--
-- Name: TABLE document_type; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT ON TABLE kernel.document_type TO themis_kernel;


--
-- Name: TABLE fiscal_period; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.fiscal_period TO themis_kernel;


--
-- Name: TABLE fiscal_year; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.fiscal_year TO themis_kernel;


--
-- Name: TABLE idempotency_record; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,DELETE ON TABLE kernel.idempotency_record TO themis_kernel;


--
-- Name: TABLE installation; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.installation TO themis_kernel;


--
-- Name: TABLE inventory_adjustment; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.inventory_adjustment TO themis_kernel;


--
-- Name: TABLE inventory_entry; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT ON TABLE kernel.inventory_entry TO themis_kernel;


--
-- Name: TABLE item; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.item TO themis_kernel;


--
-- Name: TABLE item_company; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.item_company TO themis_kernel;


--
-- Name: TABLE item_cost_state; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.item_cost_state TO themis_kernel;


--
-- Name: TABLE item_posting_group; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.item_posting_group TO themis_kernel;


--
-- Name: TABLE journal_entry; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT ON TABLE kernel.journal_entry TO themis_kernel;


--
-- Name: TABLE journal_line; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT ON TABLE kernel.journal_line TO themis_kernel;


--
-- Name: TABLE number_series; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.number_series TO themis_kernel;


--
-- Name: TABLE outbox_event; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT ON TABLE kernel.outbox_event TO themis_kernel;


--
-- Name: COLUMN outbox_event.published_at; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT UPDATE(published_at) ON TABLE kernel.outbox_event TO themis_kernel;


--
-- Name: COLUMN outbox_event.attempts; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT UPDATE(attempts) ON TABLE kernel.outbox_event TO themis_kernel;


--
-- Name: TABLE party; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.party TO themis_kernel;


--
-- Name: TABLE permission; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.permission TO themis_kernel;


--
-- Name: TABLE posting_profile_rule; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.posting_profile_rule TO themis_kernel;


--
-- Name: TABLE role; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.role TO themis_kernel;


--
-- Name: TABLE role_assignment; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.role_assignment TO themis_kernel;


--
-- Name: TABLE role_permission; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.role_permission TO themis_kernel;


--
-- Name: TABLE site; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.site TO themis_kernel;


--
-- Name: TABLE sod_conflict; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.sod_conflict TO themis_kernel;


--
-- Name: TABLE stock_balance; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.stock_balance TO themis_kernel;


--
-- Name: TABLE unit_of_measure; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.unit_of_measure TO themis_kernel;


--
-- Name: TABLE user_account; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.user_account TO themis_kernel;


--
-- Name: TABLE warehouse; Type: ACL; Schema: kernel; Owner: themis_migrator
--

GRANT SELECT,INSERT,UPDATE ON TABLE kernel.warehouse TO themis_kernel;


--
-- Name: TABLE stock_by_warehouse; Type: ACL; Schema: reporting; Owner: themis_migrator
--

GRANT SELECT ON TABLE reporting.stock_by_warehouse TO themis_reporting;


--
-- PostgreSQL database dump complete
--



-- Data written by the tag's migrator run (migrations, system catalogues, migration history); audit events excluded.

--
-- PostgreSQL database dump
--


-- Dumped from database version 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1)
-- Dumped by pg_dump version 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Data for Name: __ef_migrations_history; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261009111917_KernelPipelineTables', '10.0.13');
INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261009142943_OrganisationTables', '10.0.13');
INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261009192725_IdentityTables', '10.0.13');
INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261010152110_MasterDataTables', '10.0.13');
INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261013121553_GeneralLedgerTables', '10.0.13');
INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261017221115_DocumentFrameworkTables', '10.0.13');
INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261018115001_PostingTables', '10.0.13');
INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion") VALUES ('20261018212855_ReportingStockByWarehouse', '10.0.13');


--
-- Data for Name: chart_of_accounts; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: account; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: currency; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.currency (id, code, name, minor_units, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('2d1ea96b-91d0-7675-b46b-8a190768590d', 'SEK', 'Swedish krona', 2, '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.currency (id, code, name, minor_units, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('8d338377-796a-76f2-a125-c107cf62d7e0', 'EUR', 'Euro', 2, '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', 1, '{}');


--
-- Data for Name: party; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: company; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: document_type; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, party_role, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-97a1-79eb-8382-8828f7a23d86', 'INVENTORY_ADJUSTMENT', 'OnPost', false, true, true, NULL, '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, party_role, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-97e3-7844-b187-e8b07d8bc368', 'JOURNAL_ENTRY', 'NumberingOnly', true, false, false, NULL, '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');


--
-- Data for Name: site; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: warehouse; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: document; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: unit_of_measure; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('9091cfa4-c985-73be-8b5f-b682ff644674', 'H87', 'Piece', 'Count', 0, '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('e8383a60-461f-78d2-9f39-fa33b144761e', 'KGM', 'Kilogram', 'Mass', 3, '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('dbb58cda-374e-78ae-94f8-1f6cdf61912f', 'MTR', 'Metre', 'Length', 3, '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.431271+00', '00000000-0000-7000-8000-000000000001', 1, '{}');


--
-- Data for Name: item; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: item_posting_group; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: item_company; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: document_line; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: document_transition; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.document_transition (id, document_type, command, from_status, to_status, event_name, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-97cb-7756-8492-734f0e8172ba', 'INVENTORY_ADJUSTMENT', 'CREATE', 'Draft', 'Draft', 'INVENTORY_ADJUSTMENT_CREATED', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.document_transition (id, document_type, command, from_status, to_status, event_name, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-97e1-7e93-85ec-600212f986ac', 'INVENTORY_ADJUSTMENT', 'UPDATE', 'Draft', 'Draft', 'INVENTORY_ADJUSTMENT_UPDATED', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.document_transition (id, document_type, command, from_status, to_status, event_name, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-97e2-7619-a703-26e5222ec02a', 'INVENTORY_ADJUSTMENT', 'DISCARD', 'Draft', 'Discarded', 'INVENTORY_ADJUSTMENT_DISCARDED', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.document_transition (id, document_type, command, from_status, to_status, event_name, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-97e3-776c-a154-b57a30ba4a0b', 'INVENTORY_ADJUSTMENT', 'POST', 'Draft', 'Posted', 'INVENTORY_ADJUSTED', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');


--
-- Data for Name: fiscal_year; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: fiscal_period; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: user_account; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.user_account (id, external_subject, display_name, email, is_service_account, status, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('00000000-0000-7000-8000-000000000001', 'themis:system', 'Themis system', NULL, true, 'Active', '2026-10-18 23:19:57.356919+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.356919+00', '00000000-0000-7000-8000-000000000001', 1, '{}');


--
-- Data for Name: idempotency_record; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: installation; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: inventory_adjustment; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: journal_entry; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: inventory_entry; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: item_cost_state; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: posting_profile_rule; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: journal_line; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: number_series; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: outbox_event; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: permission; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-9661-7fc6-8b51-5f814eec7ccb', 'COMPANY.READ', 'Normal', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-96ec-7168-9b2f-7a6ec9ee5e1f', 'ITEM.CREATE', 'Normal', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-96ed-77d2-8bad-ff240e5797e2', 'ITEM.READ', 'Normal', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-96ee-7870-9ac8-0213e491e1f2', 'INVENTORY_ADJUSTMENT.CREATE', 'Normal', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-96f0-704f-8652-87217571806e', 'INVENTORY_ADJUSTMENT.UPDATE', 'Normal', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-96f1-7d45-abdd-65408fe0de93', 'INVENTORY_ADJUSTMENT.POST', 'Sensitive', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-96f2-7f77-a8e3-29a6b51adc5c', 'INVENTORY_ADJUSTMENT.DISCARD', 'Normal', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.permission (id, code, risk_level, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-96f3-733d-8fd1-f3c8cf2db21d', 'INVENTORY_ADJUSTMENT.READ', 'Normal', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');


--
-- Data for Name: role; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: role_assignment; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: role_permission; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- Data for Name: sod_conflict; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--

INSERT INTO kernel.sod_conflict (id, permission_a, permission_b, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-9707-754f-a41c-fd97ef0bf097', 'CUSTOMER.SET_CREDIT_LIMIT', 'SALES_ORDER.APPROVE_CREDIT', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.sod_conflict (id, permission_a, permission_b, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-9715-7c0f-b46a-2abc728a5fc5', 'ACCOUNT.MAINTAIN', 'MANUAL_JOURNAL.POST', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');
INSERT INTO kernel.sod_conflict (id, permission_a, permission_b, created_at, created_by, updated_at, updated_by, version, extension) VALUES ('01a15150-9715-7cc7-a3d6-03ae71a84194', 'FISCAL_PERIOD.REOPEN', 'MANUAL_JOURNAL.POST', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', '2026-10-18 23:19:57.869216+00', '00000000-0000-7000-8000-000000000001', 1, '{}');


--
-- Data for Name: stock_balance; Type: TABLE DATA; Schema: kernel; Owner: themis_migrator
--



--
-- PostgreSQL database dump complete
--


