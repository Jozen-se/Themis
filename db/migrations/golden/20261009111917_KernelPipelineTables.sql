DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'kernel') THEN
        CREATE SCHEMA kernel;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS kernel.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'kernel') THEN
        CREATE SCHEMA kernel;
    END IF;
END $EF$;

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
    CONSTRAINT pk_audit_event PRIMARY KEY (id)
);

CREATE TABLE kernel.idempotency_record (
    user_id uuid NOT NULL,
    key character varying(128) NOT NULL,
    command_name character varying(81) NOT NULL,
    request_hash character varying(64) NOT NULL,
    response jsonb NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_idempotency_record PRIMARY KEY (user_id, key)
);

CREATE TABLE kernel.outbox_event (
    id uuid NOT NULL,
    event_type character varying(81) NOT NULL,
    schema_version integer NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    published_at timestamp with time zone,
    attempts integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_outbox_event PRIMARY KEY (id)
);

CREATE INDEX ix_audit_event_entity ON kernel.audit_event (entity_type, entity_id);

CREATE INDEX ix_idempotency_record_expires_at ON kernel.idempotency_record (expires_at);

CREATE INDEX ix_outbox_event_unpublished ON kernel.outbox_event (occurred_at) WHERE published_at IS NULL;

GRANT SELECT, INSERT, DELETE ON "kernel"."idempotency_record" TO "themis_kernel";

GRANT SELECT, INSERT ON "kernel"."audit_event" TO "themis_kernel";

GRANT SELECT, INSERT ON "kernel"."outbox_event" TO "themis_kernel";

GRANT UPDATE ("published_at", "attempts") ON "kernel"."outbox_event" TO "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261009111917_KernelPipelineTables', '10.0.13');

COMMIT;
