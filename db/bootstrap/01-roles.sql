-- Themis database bootstrap: login roles, module roles, schemas and revokes (brief, Database model).
--
-- Rules and decisions: D-41 and R-DB-01 (one connection and one transaction per command, SET LOCAL ROLE to the
-- owning module's role; the login role holds no table privileges), D-26 (one schema and one role per module),
-- R-LED-06 (no UPDATE or DELETE on ledger and audit tables), invariant I-14, proven by
-- tests/Themis.Integration.Tests/DatabaseRoleModelTests.cs and PrivilegeMatrixTests.cs, which run this file.
--
-- Run once per database as a superuser. Docker Compose and Podman Compose apply it on the first start through
-- docker/postgres/initdb/10-themis-bootstrap.sh; the integration tests apply it to a fresh database. The script is
-- idempotent and plain SQL, so the tests can run it through Npgsql.
-- It sets no password (RR-65): the three LOGIN roles get theirs from 02-role-passwords.psql, which takes them from
-- psql variables and fails without them. Real values are supplied outside the repository (Build Brief, Secrets).
--
-- Roles
--   themis_migrator  LOGIN           Owns every schema, table and view. Used only by Themis.Migrator, which issues every
--                                    grant explicitly in its migrations. Never used by the running application.
--   themis_app       LOGIN NOINHERIT The application's only connection. Member of each module role WITH INHERIT FALSE,
--                                    SET TRUE: it may SET LOCAL ROLE to a module role inside a transaction and holds no
--                                    privilege of its own, so a statement that forgot to switch role is refused.
--   themis_kernel    NOLOGIN         Kernel statements: posting service, document framework, outbox relay. USAGE on
--                                    schema kernel; table privileges only as granted by migrations.
--   themis_reporting LOGIN           BI tools. USAGE on schema reporting; SELECT on its views only; nothing in kernel.
--   themis_fixture   NOLOGIN         Test-only module role, created by the integration tests, never here.
--
-- Grant hygiene
--   No default privileges. Every table and view gets its grants explicitly in the migration that creates it, through
--   the grant helper in Themis.Kernel.Persistence, and the privilege matrix test (I-14) fails on anything extra or
--   missing. Ledger and audit tables receive SELECT and INSERT only (R-LED-01, R-LED-06, R-AUD-01); DELETE exists only
--   on kernel.idempotency_record; UPDATE on kernel.outbox_event covers the delivery columns published_at and attempts
--   only. Business numbers come from number series rows, not sequences (R-DOC-07). No REFERENCES grants are needed:
--   themis_migrator owns both sides of every foreign key.
--   The one default kept (confirmed by the owner 2026-10-17, Build Brief, grant hygiene): functions created by migrations are not executable by
--   PUBLIC.
--
-- Adding a module <name> (Phase 1 and later): create role themis_<name> NOLOGIN, schema <name> owned by
-- themis_migrator with USAGE for themis_<name>, GRANT themis_<name> TO themis_app WITH INHERIT FALSE, SET TRUE, and
-- GRANT USAGE ON SCHEMA kernel plus SELECT on the specific kernel tables the module reads (D-26). Never grant a module
-- anything in another module's schema.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_migrator') THEN
        CREATE ROLE themis_migrator LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_app') THEN
        CREATE ROLE themis_app LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_kernel') THEN
        CREATE ROLE themis_kernel NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_reporting') THEN
        CREATE ROLE themis_reporting LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END
$$;

-- The application may assume the module roles inside a transaction (SET LOCAL ROLE) and inherits nothing from them.
-- Requires PostgreSQL 16 or later (brief, Database model).
GRANT themis_kernel TO themis_app WITH INHERIT FALSE, SET TRUE;

-- Nobody but the three login roles may connect to this database; nobody may create objects in public.
DO $$
BEGIN
    EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database());
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO themis_migrator, themis_app, themis_reporting', current_database());
END
$$;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Kernel schema: owned by the migrator, used by the kernel role.
CREATE SCHEMA IF NOT EXISTS kernel AUTHORIZATION themis_migrator;
REVOKE ALL ON SCHEMA kernel FROM PUBLIC;
GRANT USAGE ON SCHEMA kernel TO themis_kernel;

-- Reporting schema: views populated by the kernel, read by BI tools (P-08). Views run with their owner's rights in
-- milestone 1 (brief, Grant hygiene).
CREATE SCHEMA IF NOT EXISTS reporting AUTHORIZATION themis_migrator;
REVOKE ALL ON SCHEMA reporting FROM PUBLIC;
GRANT USAGE ON SCHEMA reporting TO themis_reporting;

-- btree_gist lets the no-overlap exclusion constraints of effective-dated rows (R-MD-02; first used by
-- kernel.posting_profile_rule in milestone 1 step 6) compare the business keys with = next to the date range with &&.
-- The extension is installed per database by a superuser, like the roles and schemas; a migration that needs it
-- fails loudly when it is missing.
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- Functions created by the migrations are not executable by PUBLIC (Build Brief, grant hygiene; confirmed by the owner 2026-10-17).
ALTER DEFAULT PRIVILEGES FOR ROLE themis_migrator IN SCHEMA kernel REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE themis_migrator IN SCHEMA reporting REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
