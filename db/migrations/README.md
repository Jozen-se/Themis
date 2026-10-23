# Migration evidence

Two sets of files keep the kernel migrations honest (Build Brief, Migrations; RR-39: a migration's SQL is fixed once
merged). Both are read by tests; neither is applied to a database by the migrator.

| Folder | Content | Test |
|---|---|---|
| `golden/` | One file per migration: the SQL `IMigrator.GenerateScript` produces for that migration alone, from the migration before it, not idempotent and with no database; the EF product version in the history row is normalised to `<product-version>`. A merged migration's file never changes; a new migration adds its own with `THEMIS_GOLDEN_RECORD=1`. | `tests/Themis.Kernel.Tests/Migrations/MigrationGoldenScriptTests.cs` (unit, no database) |
| `drift/` | One file per milestone tag: a schema-only `pg_dump` (with ACLs) of a database built by that tag's `Themis.Migrator` without a fixture, followed by the data that run wrote (migration-seeded rows, system catalogues, migration history; audit events left out because their missing command id fails the audit check constraint on a restore) as INSERT statements, with the psql `\restrict` guard lines removed. The test restores it into a bare database, runs the head migrator on it and compares the PostgreSQL catalog with a fresh database. | `tests/Themis.Integration.Tests/Hardening/MigrationDriftTests.cs` (integration) |

How to add a drift dump when a milestone tag is cut, from a checkout of the tag with the local stack running:

```bash
psql "host=localhost user=postgres password=postgres dbname=postgres" -c 'CREATE DATABASE themis_tag'
psql "host=localhost user=postgres password=postgres dbname=themis_tag" -f db/bootstrap/01-roles.sql
# From milestone 2 step 2a the role script sets no password (RR-65); a tag that has the psql script needs it as well:
psql "host=localhost user=postgres password=postgres dbname=themis_tag" -v ON_ERROR_STOP=1 -v themis_migrator_password=themis -v themis_app_password=themis -v themis_reporting_password=themis -f db/bootstrap/02-role-passwords.psql
dotnet run --project src/Themis.Migrator -- --connection "Host=localhost;Database=themis_tag;Username=themis_migrator;Password=themis"
{ pg_dump "host=localhost user=postgres password=postgres dbname=themis_tag" --schema-only --no-sync; pg_dump "host=localhost user=postgres password=postgres dbname=themis_tag" --data-only --column-inserts --no-sync --exclude-table-data=kernel.audit_event; } | grep -v '^\\restrict\|^\\unrestrict' > db/migrations/drift/<tag>.sql
```

See docs/development.md, Migrator and host, for the procedure in context.
