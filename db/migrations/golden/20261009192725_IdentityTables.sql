START TRANSACTION;
CREATE TABLE kernel.permission (
    id uuid NOT NULL,
    code character varying(81) NOT NULL,
    risk_level character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_permission PRIMARY KEY (id),
    CONSTRAINT ak_permission_code UNIQUE (code),
    CONSTRAINT ck_permission_risk_level CHECK (risk_level IN ('Normal', 'Sensitive'))
);

CREATE TABLE kernel.role (
    id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name character varying(200) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_role PRIMARY KEY (id)
);

CREATE TABLE kernel.sod_conflict (
    id uuid NOT NULL,
    permission_a character varying(81) NOT NULL,
    permission_b character varying(81) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_sod_conflict PRIMARY KEY (id),
    CONSTRAINT ck_sod_conflict_order CHECK (permission_a < permission_b)
);

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
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_user_account PRIMARY KEY (id),
    CONSTRAINT ck_user_account_status CHECK (status IN ('Active', 'Disabled'))
);

CREATE TABLE kernel.role_permission (
    id uuid NOT NULL,
    role_id uuid NOT NULL,
    permission_code character varying(81) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_role_permission PRIMARY KEY (id),
    CONSTRAINT fk_role_permission_permission FOREIGN KEY (permission_code) REFERENCES kernel.permission (code) ON DELETE RESTRICT,
    CONSTRAINT fk_role_permission_role FOREIGN KEY (role_id) REFERENCES kernel.role (id) ON DELETE RESTRICT
);

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
    version bigint NOT NULL DEFAULT 1,
    extension jsonb NOT NULL DEFAULT ('{}'::jsonb),
    CONSTRAINT pk_role_assignment PRIMARY KEY (id),
    CONSTRAINT ck_role_assignment_validity CHECK (valid_to IS NULL OR valid_to > valid_from),
    CONSTRAINT fk_role_assignment_assigned_by FOREIGN KEY (assigned_by) REFERENCES kernel.user_account (id) ON DELETE RESTRICT,
    CONSTRAINT fk_role_assignment_company FOREIGN KEY (company_id) REFERENCES kernel.company (id) ON DELETE RESTRICT,
    CONSTRAINT fk_role_assignment_role FOREIGN KEY (role_id) REFERENCES kernel.role (id) ON DELETE RESTRICT,
    CONSTRAINT fk_role_assignment_site_company FOREIGN KEY (site_id, company_id) REFERENCES kernel.site (id, company_id) ON DELETE RESTRICT,
    CONSTRAINT fk_role_assignment_user FOREIGN KEY (user_id) REFERENCES kernel.user_account (id) ON DELETE RESTRICT
);

CREATE INDEX "IX_audit_event_user_id" ON kernel.audit_event (user_id);

CREATE UNIQUE INDEX ux_permission_code ON kernel.permission (code);

CREATE UNIQUE INDEX ux_role_code ON kernel.role (code);

CREATE INDEX "IX_role_assignment_assigned_by" ON kernel.role_assignment (assigned_by);

CREATE INDEX "IX_role_assignment_company_id" ON kernel.role_assignment (company_id);

CREATE INDEX "IX_role_assignment_role_id" ON kernel.role_assignment (role_id);

CREATE INDEX "IX_role_assignment_site_id_company_id" ON kernel.role_assignment (site_id, company_id);

CREATE INDEX ix_role_assignment_user_company ON kernel.role_assignment (user_id, company_id);

CREATE INDEX "IX_role_permission_permission_code" ON kernel.role_permission (permission_code);

CREATE UNIQUE INDEX ux_role_permission_role_code ON kernel.role_permission (role_id, permission_code);

CREATE UNIQUE INDEX ux_sod_conflict_pair ON kernel.sod_conflict (permission_a, permission_b);

CREATE UNIQUE INDEX ux_user_account_external_subject ON kernel.user_account (external_subject);

INSERT INTO kernel.user_account (id, external_subject, display_name, email, is_service_account, status, created_at, created_by, updated_at, updated_by) VALUES ('00000000-0000-7000-8000-000000000001', 'themis:system', 'Themis system', NULL, true, 'Active', now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001') ON CONFLICT (id) DO NOTHING;

INSERT INTO kernel.audit_event (id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'User', '00000000-0000-7000-8000-000000000001', '{"externalSubject": {"before": null, "after": "themis:system"}, "displayName": {"before": null, "after": "Themis system"}, "isServiceAccount": {"before": null, "after": "True"}, "status": {"before": null, "after": "Active"}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '00000000-0000-7000-8000-000000000001');

ALTER TABLE kernel.audit_event ADD CONSTRAINT fk_audit_event_user FOREIGN KEY (user_id) REFERENCES kernel.user_account (id) ON DELETE RESTRICT;

ALTER TABLE kernel.idempotency_record ADD CONSTRAINT fk_idempotency_record_user FOREIGN KEY (user_id) REFERENCES kernel.user_account (id) ON DELETE RESTRICT;

GRANT SELECT, INSERT, UPDATE ON "kernel"."user_account" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."permission" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."role" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."role_permission" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."role_assignment" TO "themis_kernel";

GRANT SELECT, INSERT, UPDATE ON "kernel"."sod_conflict" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261009192725_IdentityTables', '10.0.13');

COMMIT;
