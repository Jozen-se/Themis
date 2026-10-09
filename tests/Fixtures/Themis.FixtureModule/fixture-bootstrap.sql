-- Test-only module role and schema for the fixture module (brief, Database model). Applied by the integration tests
-- after db/bootstrap/01-roles.sql, never by the compose stack or a real installation.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'themis_fixture') THEN
        CREATE ROLE themis_fixture NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END
$$;

GRANT themis_fixture TO themis_app WITH INHERIT FALSE, SET TRUE;

CREATE SCHEMA IF NOT EXISTS fixture AUTHORIZATION themis_migrator;
REVOKE ALL ON SCHEMA fixture FROM PUBLIC;
GRANT USAGE ON SCHEMA fixture TO themis_fixture;
GRANT USAGE ON SCHEMA kernel TO themis_fixture;
