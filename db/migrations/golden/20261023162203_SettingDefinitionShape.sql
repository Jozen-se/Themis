START TRANSACTION;
ALTER TABLE kernel.setting_definition ALTER COLUMN value_type DROP NOT NULL;

ALTER TABLE kernel.setting_definition ADD CONSTRAINT ck_setting_definition_shape CHECK ((backing IN ('Setting', 'Column') AND value_type IS NOT NULL) OR (backing IN ('Data', 'Planned') AND value_type IS NULL AND default_value IS NULL AND min IS NULL AND max IS NULL AND allowed_values IS NULL));

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261023162203_SettingDefinitionShape', '10.0.13');

COMMIT;
