START TRANSACTION;
DROP INDEX kernel."IX_company_party_id";

ALTER TABLE kernel.number_series ALTER COLUMN company_id DROP NOT NULL;

ALTER TABLE kernel.company ADD organisation_number_normalised character varying(50);

UPDATE kernel.company SET organisation_number_normalised = regexp_replace(upper(organisation_number), '[^[:alnum:]]', '', 'g');

ALTER TABLE kernel.company ALTER COLUMN organisation_number_normalised SET NOT NULL;

ALTER TABLE kernel.number_series ADD CONSTRAINT ck_number_series_installation_continuous CHECK (company_id IS NOT NULL OR scope = 'Continuous');

CREATE UNIQUE INDEX ux_company_country_organisation_number ON kernel.company (country_code, organisation_number_normalised);

CREATE UNIQUE INDEX ux_company_party ON kernel.company (party_id);

INSERT INTO kernel.document_type (id, code, number_assigned, gapless, posts_inventory, posts_general_ledger, party_role, created_at, created_by, updated_at, updated_by) SELECT '00000000-0000-7000-8000-000000000a01', 'PARTY', 'NumberingOnly', false, false, false, NULL, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001' WHERE NOT EXISTS (SELECT 1 FROM kernel.document_type WHERE code = 'PARTY');

INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), '00000000-0000-7000-8000-000000000a00', now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'DocumentType', '00000000-0000-7000-8000-000000000a01', '{"code": {"before": null, "after": "PARTY"}, "numberAssigned": {"before": null, "after": "NumberingOnly"}, "gapless": {"before": null, "after": "False"}, "postsInventory": {"before": null, "after": "False"}, "postsGeneralLedger": {"before": null, "after": "False"}, "partyRole": {"before": null, "after": null}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '00000000-0000-7000-8000-000000000a01');

INSERT INTO kernel.number_series (id, company_id, document_type, scope, fiscal_year_id, prefix, padding, next_number, gapless, type_requires_gapless, created_at, created_by, updated_at, updated_by) SELECT '00000000-0000-7000-8000-000000000a02', NULL, 'PARTY', 'Continuous', NULL, 'P-', 6, 1, false, false, now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001' WHERE NOT EXISTS (SELECT 1 FROM kernel.number_series WHERE company_id IS NULL AND document_type = 'PARTY');

INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), '00000000-0000-7000-8000-000000000a00', now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'NumberSeries', '00000000-0000-7000-8000-000000000a02', '{"companyId": {"before": null, "after": null}, "documentType": {"before": null, "after": "PARTY"}, "scope": {"before": null, "after": "Continuous"}, "fiscalYearId": {"before": null, "after": null}, "prefix": {"before": null, "after": "P-"}, "padding": {"before": null, "after": "6"}, "nextNumber": {"before": null, "after": "1"}, "gapless": {"before": null, "after": "False"}}'::jsonb WHERE NOT EXISTS (SELECT 1 FROM kernel.audit_event WHERE command_name = 'SYSTEM.MIGRATE' AND entity_id = '00000000-0000-7000-8000-000000000a02');

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261024165237_OrganisationCommands', '10.0.13');

COMMIT;
