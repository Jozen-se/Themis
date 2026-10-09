-- Themis database role model.
--
-- Rules and decisions: R-DB-01 and D-41 (one connection and one transaction per command, SET LOCAL ROLE to the
-- owning module's role; the login role holds no table privileges), D-26 (one schema and one role per module),
-- R-LED-06 (no UPDATE or DELETE on ledger and audit tables), invariant I-14 (proven by
-- tests/Themis.IntegrationTests/DatabaseRoleModelTests.cs, which runs this very file).
--
-- The script is idempotent and runs as a superuser against the application database (Docker Compose applies it
-- from /docker-entrypoint-initdb.d on first start; the integration tests apply it to a fresh container).
-- Passwords below are development defaults for the local Compose stack only. Production sets them with
-- ALTER ROLE ... PASSWORD from secrets supplied through the environment, never from this file.
--
-- Roles
--   themis_owner   LOGIN   Owns every schema and table. Used by migrations only; never by the running application.
--   themis_app     LOGIN   The only role the application connects as. NOINHERIT: membership in the module roles lets
--                          it SET LOCAL ROLE, but it inherits none of their privileges, so a statement that forgot to
--                          switch role is refused by the database.
--   themis_kernel  NOLOGIN The kernel module's role: privileges on schema "kernel" only.
--
-- Adding a module <name> (Phase 1 and later): create role themis_<name> NOLOGIN, schema <name> owned by
-- themis_owner with USAGE for themis_<name>, the same default privileges as below, GRANT themis_<name> TO themis_app,
-- and GRANT USAGE ON SCHEMA kernel plus SELECT and REFERENCES on the specific kernel tables the module may read or
-- reference (D-26). Never grant a module anything in another module's schema.
--
-- Privilege defaults
--   Tables created by migrations in a module schema give the module role SELECT and INSERT only. UPDATE and DELETE
--   are granted per table by the migration that creates a mutable table (master data, documents, caches).
--   Ledger and audit tables (JournalEntry, JournalLine, InventoryEntry, AuditEvent, OutboxEvent) never receive them
--   (R-LED-01, R-LED-06, R-AUD-01). A forgotten grant fails loudly in tests; a forgotten revoke would not, which is
--   why the safe default is the narrow one (P-06).

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_owner') THEN
        CREATE ROLE themis_owner LOGIN PASSWORD 'themis' NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_app') THEN
        CREATE ROLE themis_app LOGIN PASSWORD 'themis' NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_kernel') THEN
        CREATE ROLE themis_kernel NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END
$$;

-- The application may assume the module roles inside a transaction (SET LOCAL ROLE), nothing more.
GRANT themis_kernel TO themis_app;

-- Nobody but the named roles may connect to or create objects in this database.
DO $$
BEGIN
    EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database());
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO themis_owner, themis_app', current_database());
    EXECUTE format('GRANT TEMPORARY ON DATABASE %I TO themis_app', current_database());
END
$$;

-- The public schema is not used by Themis. Ownership stays with the superuser; nobody may create objects in it.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Kernel schema: owned by the migration role, used by the kernel role.
CREATE SCHEMA IF NOT EXISTS kernel AUTHORIZATION themis_owner;
REVOKE ALL ON SCHEMA kernel FROM PUBLIC;
GRANT USAGE ON SCHEMA kernel TO themis_kernel;

-- Default privileges for objects that themis_owner (the migrations) will create in schema kernel.
ALTER DEFAULT PRIVILEGES FOR ROLE themis_owner IN SCHEMA kernel GRANT SELECT, INSERT ON TABLES TO themis_kernel;
ALTER DEFAULT PRIVILEGES FOR ROLE themis_owner IN SCHEMA kernel GRANT USAGE, SELECT ON SEQUENCES TO themis_kernel;
ALTER DEFAULT PRIVILEGES FOR ROLE themis_owner IN SCHEMA kernel GRANT EXECUTE ON FUNCTIONS TO themis_kernel;
ALTER DEFAULT PRIVILEGES FOR ROLE themis_owner IN SCHEMA kernel REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
