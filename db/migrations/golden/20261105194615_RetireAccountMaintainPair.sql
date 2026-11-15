START TRANSACTION;
INSERT INTO kernel.audit_event (id, command_id, occurred_at, user_id, command_name, entity_type, entity_id, changes) SELECT gen_random_uuid(), '00000000-0000-7000-8000-000000000b00', now(), '00000000-0000-7000-8000-000000000001', 'SYSTEM.MIGRATE', 'SodConflict', c.id, jsonb_build_object('permissionA', jsonb_build_object('before', c.permission_a, 'after', NULL), 'permissionB', jsonb_build_object('before', c.permission_b, 'after', NULL)) FROM kernel.sod_conflict c WHERE c.permission_a = 'ACCOUNT.MAINTAIN' AND c.permission_b = 'MANUAL_JOURNAL.POST';

DELETE FROM kernel.sod_conflict WHERE permission_a = 'ACCOUNT.MAINTAIN' AND permission_b = 'MANUAL_JOURNAL.POST';

INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
VALUES ('20261105194615_RetireAccountMaintainPair', '10.0.14');

COMMIT;
