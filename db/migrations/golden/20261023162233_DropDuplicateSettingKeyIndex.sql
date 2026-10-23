START TRANSACTION;
DROP INDEX kernel.ux_setting_definition_key;

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261023162233_DropDuplicateSettingKeyIndex', '10.0.13');

COMMIT;
