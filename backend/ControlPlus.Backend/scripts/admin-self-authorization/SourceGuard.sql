BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
  IF current_setting('transaction_read_only') <> 'on'
     OR current_database() <> 'controlplus'
     OR current_user <> 'postgres'
     OR current_setting('server_version_num')::int NOT BETWEEN 180000 AND 189999
     OR NOT ((SELECT count(*) FROM public."__EFMigrationsHistory") = 15
               AND (SELECT "MigrationId" FROM public."__EFMigrationsHistory"
                    ORDER BY "MigrationId" DESC LIMIT 1) = '20261102020000_CategoryProductReferencesV1'
          OR (SELECT count(*) FROM public."__EFMigrationsHistory") = 16
               AND (SELECT "MigrationId" FROM public."__EFMigrationsHistory"
                    ORDER BY "MigrationId" DESC LIMIT 1) = '20261104010000_AdminSelfSaleCancellationV1'
          OR (SELECT count(*) FROM public."__EFMigrationsHistory") = 17
               AND (SELECT "MigrationId" FROM public."__EFMigrationsHistory"
                    ORDER BY "MigrationId" DESC LIMIT 1) = '20261104020000_AdminSelfCashDifferenceV1')
     OR EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid()
                  AND backend_type = 'client backend')
  THEN RAISE EXCEPTION 'Admin self source guard rejected'; END IF;
END $check$;
SELECT 'ADMIN_SELF_SOURCE_GUARD_OK' AS status;
ROLLBACK;
