START TRANSACTION;
ALTER TABLE kernel.warehouse ADD CONSTRAINT "AK_warehouse_id_company_id" UNIQUE (id, company_id);

CREATE TABLE kernel.currency (
    id uuid NOT NULL,
    code character varying(3) NOT NULL,
    name character varying(200) NOT NULL,
    minor_units integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_currency PRIMARY KEY (id),
    CONSTRAINT ak_currency_code UNIQUE (code),
    CONSTRAINT ck_currency_minor_units CHECK (minor_units BETWEEN 0 AND 3)
);

CREATE TABLE kernel.item_posting_group (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_item_posting_group PRIMARY KEY (id),
    CONSTRAINT ak_item_posting_group_code UNIQUE (code)
);

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
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_unit_of_measure PRIMARY KEY (id),
    CONSTRAINT ak_unit_of_measure_code UNIQUE (code),
    CONSTRAINT ck_unit_of_measure_decimals CHECK (decimals BETWEEN 0 AND 6),
    CONSTRAINT ck_unit_of_measure_dimension CHECK (dimension IN ('Count', 'Mass', 'Length', 'Volume', 'Time'))
);

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
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_item PRIMARY KEY (id),
    CONSTRAINT ck_item_item_type CHECK (item_type IN ('Stocked', 'NonStocked', 'Service')),
    CONSTRAINT ck_item_lot_tracking CHECK (lot_tracking IN ('None', 'Required')),
    CONSTRAINT ck_item_min_order_qty CHECK (min_order_qty IS NULL OR min_order_qty > 0),
    CONSTRAINT ck_item_net_weight_kg CHECK (net_weight_kg IS NULL OR net_weight_kg >= 0),
    CONSTRAINT ck_item_sales_multiple CHECK (sales_multiple IS NULL OR sales_multiple > 0),
    CONSTRAINT fk_item_base_unit FOREIGN KEY (base_unit) REFERENCES kernel.unit_of_measure (code) ON DELETE RESTRICT
);

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
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_item_company PRIMARY KEY (id),
    CONSTRAINT ck_item_company_costing_method CHECK (costing_method IN ('MovingAverage')),
    CONSTRAINT ck_item_company_status CHECK (status IN ('Active', 'Blocked', 'Inactive')),
    CONSTRAINT ck_item_company_tax_class CHECK (tax_class IN ('Standard', 'Reduced', 'Zero', 'Exempt')),
    CONSTRAINT fk_item_company_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_item_company_default_warehouse_company FOREIGN KEY (default_warehouse_id, company_id) REFERENCES kernel.warehouse (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_item_company_item FOREIGN KEY (item_id) REFERENCES kernel.item (id) ON DELETE RESTRICT,
    CONSTRAINT fk_item_company_item_posting_group FOREIGN KEY (item_posting_group) REFERENCES kernel.item_posting_group (code) ON DELETE RESTRICT
);

CREATE INDEX "IX_company_base_currency" ON kernel.company (base_currency);

CREATE INDEX "IX_item_base_unit" ON kernel.item (base_unit);

CREATE UNIQUE INDEX ux_item_item_number ON kernel.item (item_number);

CREATE INDEX "IX_item_company_company_id" ON kernel.item_company (company_id);

CREATE INDEX "IX_item_company_default_warehouse_id_company_id" ON kernel.item_company (default_warehouse_id, company_id);

CREATE INDEX "IX_item_company_item_posting_group" ON kernel.item_company (item_posting_group);

CREATE UNIQUE INDEX ux_item_company_item_company ON kernel.item_company (item_id, company_id);

INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by) VALUES ('9091cfa4-c985-73be-8b5f-b682ff644674', 'H87', 'Piece', 'Count', 0, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001') ON CONFLICT (id) DO NOTHING;

INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'UnitOfMeasure', '9091cfa4-c985-73be-8b5f-b682ff644674', '{"code": {"before": null, "after": "H87"}, "name": {"before": null, "after": "Piece"}, "dimension": {"before": null, "after": "Count"}, "decimals": {"before": null, "after": "0"}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '9091cfa4-c985-73be-8b5f-b682ff644674');

INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by) VALUES ('e8383a60-461f-78d2-9f39-fa33b144761e', 'KGM', 'Kilogram', 'Mass', 3, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001') ON CONFLICT (id) DO NOTHING;

INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'UnitOfMeasure', 'e8383a60-461f-78d2-9f39-fa33b144761e', '{"code": {"before": null, "after": "KGM"}, "name": {"before": null, "after": "Kilogram"}, "dimension": {"before": null, "after": "Mass"}, "decimals": {"before": null, "after": "3"}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = 'e8383a60-461f-78d2-9f39-fa33b144761e');

INSERT INTO kernel.unit_of_measure (id, code, name, dimension, decimals, created_at, created_by, updated_at, updated_by) VALUES ('dbb58cda-374e-78ae-94f8-1f6cdf61912f', 'MTR', 'Metre', 'Length', 3, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001') ON CONFLICT (id) DO NOTHING;

INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'UnitOfMeasure', 'dbb58cda-374e-78ae-94f8-1f6cdf61912f', '{"code": {"before": null, "after": "MTR"}, "name": {"before": null, "after": "Metre"}, "dimension": {"before": null, "after": "Length"}, "decimals": {"before": null, "after": "3"}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = 'dbb58cda-374e-78ae-94f8-1f6cdf61912f');

INSERT INTO kernel.currency (id, code, name, minor_units, created_at, created_by, updated_at, updated_by) VALUES ('2d1ea96b-91d0-7675-b46b-8a190768590d', 'SEK', 'Swedish krona', 2, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001') ON CONFLICT (id) DO NOTHING;

INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'Currency', '2d1ea96b-91d0-7675-b46b-8a190768590d', '{"code": {"before": null, "after": "SEK"}, "name": {"before": null, "after": "Swedish krona"}, "minorUnits": {"before": null, "after": "2"}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '2d1ea96b-91d0-7675-b46b-8a190768590d');

INSERT INTO kernel.currency (id, code, name, minor_units, created_at, created_by, updated_at, updated_by) VALUES ('8d338377-796a-76f2-a125-c107cf62d7e0', 'EUR', 'Euro', 2, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001') ON CONFLICT (id) DO NOTHING;

INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'Currency', '8d338377-796a-76f2-a125-c107cf62d7e0', '{"code": {"before": null, "after": "EUR"}, "name": {"before": null, "after": "Euro"}, "minorUnits": {"before": null, "after": "2"}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '8d338377-796a-76f2-a125-c107cf62d7e0');

ALTER TABLE kernel.company ADD CONSTRAINT fk_company_base_currency FOREIGN KEY (base_currency) REFERENCES kernel.currency (code) ON DELETE RESTRICT;

GRANT SELECT, INSERT, UPDATE ON "kernel"."unit_of_measure" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."currency" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."item_posting_group" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."item" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."item_company" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261010152110_MasterDataTables', '10.0.13');

COMMIT;
