START TRANSACTION;
CREATE TABLE kernel.setting_definition (
    id uuid NOT NULL,
    key character varying(100) NOT NULL,
    value_type character varying(20) NOT NULL,
    default_value text,
    min character varying(100),
    max character varying(100),
    allowed_values text,
    levels character varying(100) NOT NULL,
    history_bearing boolean NOT NULL,
    locked_reason text,
    backing character varying(20) NOT NULL,
    column_name character varying(100),
    phase character varying(100),
    planned_levels character varying(100),
    rule_id character varying(20) NOT NULL,
    decision_id character varying(20),
    description text NOT NULL,
    retired boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_setting_definition PRIMARY KEY (id),
    CONSTRAINT ak_setting_definition_key UNIQUE (key),
    CONSTRAINT ck_setting_definition_backing CHECK (backing IN ('Setting', 'Column', 'Data', 'Planned')),
    CONSTRAINT ck_setting_definition_value_type CHECK (value_type IN ('Integer', 'Decimal', 'Boolean', 'Code', 'Duration'))
);

CREATE TABLE kernel.setting_value (
    id uuid NOT NULL,
    definition_key character varying(100) NOT NULL,
    level character varying(20) NOT NULL,
    company_id uuid,
    site_id uuid,
    warehouse_id uuid,
    kind character varying(20) NOT NULL,
    value text,
    valid_from date,
    version bigint NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    recorded_by uuid NOT NULL,
    command_id uuid NOT NULL,
    CONSTRAINT pk_setting_value PRIMARY KEY (id),
    CONSTRAINT ck_setting_value_kind CHECK (kind IN ('Set', 'Cleared')),
    CONSTRAINT ck_setting_value_level CHECK (level IN ('Installation', 'Company', 'Site', 'Warehouse')),
    CONSTRAINT ck_setting_value_target CHECK ((level = 'Installation' AND company_id IS NULL AND site_id IS NULL AND warehouse_id IS NULL) OR (level = 'Company' AND company_id IS NOT NULL AND site_id IS NULL AND warehouse_id IS NULL) OR (level = 'Site' AND company_id IS NOT NULL AND site_id IS NOT NULL AND warehouse_id IS NULL) OR (level = 'Warehouse' AND company_id IS NOT NULL AND site_id IS NULL AND warehouse_id IS NOT NULL)),
    CONSTRAINT ck_setting_value_value CHECK ((kind = 'Set' AND value IS NOT NULL) OR (kind = 'Cleared' AND value IS NULL)),
    CONSTRAINT ck_setting_value_version CHECK (version >= 1),
    CONSTRAINT fk_setting_value_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_setting_value_definition FOREIGN KEY (definition_key) REFERENCES kernel.setting_definition (key) ON DELETE RESTRICT,
    CONSTRAINT fk_setting_value_recorded_by FOREIGN KEY (recorded_by) REFERENCES kernel.user_account (id) ON DELETE RESTRICT,
    CONSTRAINT fk_setting_value_site_company FOREIGN KEY (site_id, company_id) REFERENCES kernel.site (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_setting_value_warehouse_company FOREIGN KEY (warehouse_id, company_id) REFERENCES kernel.warehouse (id, company_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_setting_definition_key ON kernel.setting_definition (key);

CREATE INDEX "IX_setting_value_company_id" ON kernel.setting_value (company_id);

CREATE INDEX "IX_setting_value_recorded_by" ON kernel.setting_value (recorded_by);

CREATE INDEX "IX_setting_value_site_id_company_id" ON kernel.setting_value (site_id, company_id);

CREATE INDEX "IX_setting_value_warehouse_id_company_id" ON kernel.setting_value (warehouse_id, company_id);

CREATE UNIQUE INDEX ux_setting_value_slot_version ON kernel.setting_value (definition_key, level, company_id, site_id, warehouse_id, version) NULLS NOT DISTINCT;

GRANT SELECT ON "kernel"."setting_definition" TO "themis_kernel";

GRANT SELECT, INSERT ON "kernel"."setting_value" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261019180706_SettingsTables', '10.0.13');

COMMIT;
