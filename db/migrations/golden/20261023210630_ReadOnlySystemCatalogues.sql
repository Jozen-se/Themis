START TRANSACTION;
REVOKE INSERT, UPDATE ON "kernel"."permission" FROM "themis_kernel";

REVOKE INSERT, UPDATE ON "kernel"."sod_conflict" FROM "themis_kernel";

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261023210630_ReadOnlySystemCatalogues', '10.0.13');

COMMIT;
