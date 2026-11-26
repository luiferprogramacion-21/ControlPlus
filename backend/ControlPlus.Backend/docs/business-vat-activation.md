# Activación del IVA del negocio (historial 19→21)

> **Estado vigente (21 de noviembre de 2026):** las tres migraciones de IVA ya están aplicadas en `controlplus` (historial 19→21 el 11 de noviembre y 21→22 el 12 de noviembre de 2026). Los bloques «pendiente de ejecución» de abajo son históricos: **no repetir ninguno**. El IVA se quitó de la operación el 18 de noviembre de 2026.


**Activación ejecutada por el operador el 11 de noviembre de 2026.** La evidencia está en `%USERPROFILE%\ControlPlus-Backups\BusinessVat-20261111T140623Z-d62b0903500246c79110ba1fa64eb681` y registra `BUSINESS_VAT_ACTIVATION_OK`. El agente no consultó `controlplus` ni ejecutó migraciones. Objetivos aplicados, en este orden:

1. `20261110010000_BusinessVatSettingV1`: columnas y CHECK de `configuracion.establecimiento`, catálogo `TAXES.MANAGE` y una concesión de plantilla a Administrador.
2. `20261110020000_BusinessVatWholePesoSalesV1`: reemplaza `ck_detalle_venta_iva_incluido` para admitir redondeo a peso entero además del histórico a cuatro decimales.

La línea base exigía **19 migraciones** y `20261106010000_ProductPhysicalDeletionPrivilegesV1` como última. El historial final es **21 migraciones**. El respaldo tiene SHA-256 `D585680B77F5C4934771DECD8FE92CFFE9103E7578AA7863DF962A29254179A8` y la API congelada SHA-256 `640409C133C3BD9F7ECC90FD1109F6F6F0E7E2E19B33785E4EED6CAF8FC0EE77`. Las 5 ventas y 12 líneas existentes conservaron huellas idénticas antes y después de cada migración. Ambos postflight dieron OK, incluido el acceso de `controlplus_app`. **No repetir estas migraciones ni ejecutar Down.**

Después de la activación, la API respondió HTTP 200 en `/api/health`, `/api/health/database` y `/openapi/v1.json`; `/api/settings/vat` respondió 401 sin sesión. El operador verificó el panel IVA de Configuración con el Administrador e hizo una venta de contado de 5000 con IVA activado. Aprobó su desglose, el reporte C3 y el PDF.

## Ventana y ejecución (registro histórico; no volver a ejecutar)

El siguiente bloque documenta el procedimiento ya ejecutado. Se conserva como evidencia histórica y **no debe volver a ejecutarse** sobre `controlplus`. La ventana exigía detener API y frontend, confirmar que no hubiera otros clientes que escribieran y mantener PostgreSQL 18 activo. El proceso de la API se identificaba por su línea de comando y se detenía desde su consola con Ctrl+C (o `Stop-Process -Id <PID>` tras verificarlo). El procedimiento compilaba en serie la versión nueva antes de la ventana y congelaba el directorio `publish`, sin recompilar entre confirmaciones. Se ejecutaba desde `C:\ControlPlus\backend\ControlPlus.Backend` con `dotnet`, `psql`, `pg_dump` y `pg_restore` de PostgreSQL 18 en PATH; las credenciales de `postgres` se solicitaban ocultas. La carpeta de evidencia bajo `%USERPROFILE%\ControlPlus-Backups` debe permanecer privada.

```powershell
& {
  $ErrorActionPreference = 'Stop'
  Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
  $targets = @('20261110010000_BusinessVatSettingV1', '20261110020000_BusinessVatWholePesoSalesV1')
  $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
  $evidence = Join-Path $env:USERPROFILE "ControlPlus-Backups\BusinessVat-$stamp-$([guid]::NewGuid().ToString('N'))"
  New-Item -ItemType Directory -Path $evidence -Force | Out-Null
  $publish = Join-Path $evidence 'api-bin'
  dotnet publish .\ControlPlus.Api\ControlPlus.Api.csproj -c Release -o $publish -m:1 /p:BuildInParallel=false /warnaserror
  if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación; detener.' }
  $apiDll = Join-Path $publish 'ControlPlus.Api.dll'
  $binaryHash = (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash
  $password = Read-Host 'Contraseña de postgres' -AsSecureString
  $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
  try { $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
  $env:PGHOST = '127.0.0.1'; $env:PGPORT = '5432'; $env:PGUSER = 'postgres'; $env:PGDATABASE = 'controlplus'
  $env:PGOPTIONS = '-c default_transaction_read_only=on'
  $env:ConnectionStrings__ControlPlusDb = "Host=127.0.0.1;Port=5432;Database=controlplus;Username=postgres;Password=$($env:PGPASSWORD)"
  function ReadSql([string]$name, [string]$sql) {
    $path = Join-Path $evidence "$name.sql"
    [IO.File]::WriteAllText($path, $sql, [Text.UTF8Encoding]::new($false))
    $out = & psql -X -v ON_ERROR_STOP=1 -A -t -f $path 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Falló $name`: $out" }
    $out | Set-Content -LiteralPath (Join-Path $evidence "$name.txt") -Encoding UTF8
    return @($out)
  }
  $historySql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT count(*)::text || '|' || max("MigrationId") FROM public."__EFMigrationsHistory";
ROLLBACK;
'@
  function AssertHistory([int]$count, [string]$last, [string]$stage) {
    $rows = @(ReadSql "history-$stage" $historySql | Where-Object { $_ -match '^\d+\|' })
    if ($rows.Count -ne 1 -or $rows[0] -cne "$count|$last") { throw "Historial inesperado en $stage`: $rows" }
  }
  $salesSql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT 'venta|' || count(*) || '|' || coalesce(md5(string_agg(md5(row_to_json(v)::text), '' ORDER BY v.id::text)), md5('')) FROM ventas.venta v;
SELECT 'detalle_venta|' || count(*) || '|' || coalesce(md5(string_agg(md5(row_to_json(d)::text), '' ORDER BY d.id::text)), md5('')) FROM ventas.detalle_venta d;
ROLLBACK;
'@
  try {
    $guard = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
 IF current_database() <> 'controlplus' OR current_user <> 'postgres'
    OR current_setting('transaction_read_only') <> 'on'
    OR current_setting('server_version_num')::int NOT BETWEEN 180000 AND 189999
    OR (SELECT count(*) FROM public."__EFMigrationsHistory") <> 19
    OR (SELECT max("MigrationId") FROM public."__EFMigrationsHistory") <> '20261106010000_ProductPhysicalDeletionPrivilegesV1'
    OR EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend')
    OR EXISTS (SELECT 1 FROM seguridad.permiso WHERE codigo='TAXES.MANAGE')
    OR EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='configuracion' AND table_name='establecimiento' AND column_name='iva_negocio_habilitado')
 THEN RAISE EXCEPTION 'BUSINESS_VAT_PREFLIGHT_REJECTED'; END IF;
END $check$;
SELECT 'BUSINESS_VAT_PREFLIGHT_OK';
ROLLBACK;
'@
    if (@(ReadSql 'preflight' $guard | Where-Object { $_ -eq 'BUSINESS_VAT_PREFLIGHT_OK' }).Count -ne 1) { throw 'Preflight incompleto.' }
    AssertHistory 19 '20261106010000_ProductPhysicalDeletionPrivilegesV1' 'before'
    $salesBefore = @(ReadSql 'sales-before' $salesSql | Where-Object { $_ -match '^(venta|detalle_venta)\|' })
    if ($salesBefore.Count -ne 2) { throw 'Huella inicial incompleta.' }
    $dump = Join-Path $evidence 'controlplus-before.dump'
    & pg_dump -Fc --no-owner --no-acl -f $dump controlplus
    if ($LASTEXITCODE -ne 0) { throw 'Falló pg_dump.' }
    $backupHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $backupHash | Set-Content -LiteralPath (Join-Path $evidence 'backup.sha256') -Encoding ASCII
    & pg_restore --list $dump > (Join-Path $evidence 'backup.toc.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Respaldo sin TOC legible.' }
    & pg_restore --file NUL $dump
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) { throw 'Falló verificación del respaldo.' }
    AssertHistory 19 '20261106010000_ProductPhysicalDeletionPrivilegesV1' 'after-backup'
    $salesAfterBackup = @(ReadSql 'sales-after-backup' $salesSql | Where-Object { $_ -match '^(venta|detalle_venta)\|' })
    if (($salesAfterBackup -join "`n") -cne ($salesBefore -join "`n")) { throw 'Ventas cambiaron durante el respaldo.' }
    Remove-Item Env:PGOPTIONS
    $env:CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
    for ($i=0; $i -lt 2; $i++) {
      if ((Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $binaryHash -or
          (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) { throw 'Cambió binario o respaldo.' }
      $target = $targets[$i]
      $output = & dotnet $apiDll --migrate-to $target 2>&1
      $code = $LASTEXITCODE
      $output | Set-Content -LiteralPath (Join-Path $evidence "apply-$target.txt") -Encoding UTF8
      if ($code -ne 0) { throw "Migrador falló en $target; no reintentar automáticamente." }
      $env:PGOPTIONS = '-c default_transaction_read_only=on'
      AssertHistory (20+$i) $target "post-$i"
      $post = if ($i -eq 0) { @'
BEGIN READ ONLY;
DO $check$ DECLARE column_name text; BEGIN
 IF (SELECT count(*) FROM seguridad.permiso WHERE codigo='TAXES.MANAGE') <> 1
 OR (SELECT count(*) FROM seguridad.rol_permiso rp JOIN seguridad.rol r ON r.id=rp.rol_id JOIN seguridad.permiso p ON p.id=rp.permiso_id WHERE p.codigo='TAXES.MANAGE' AND r.codigo='ADMINISTRADOR') <> 1
 OR (SELECT count(*) FROM seguridad.rol_permiso rp JOIN seguridad.permiso p ON p.id=rp.permiso_id WHERE p.codigo='TAXES.MANAGE') <> 1
 OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='configuracion.establecimiento'::regclass AND conname='ck_establecimiento_iva_negocio')
 THEN RAISE EXCEPTION 'BUSINESS_VAT_SETTING_POSTFLIGHT_REJECTED'; END IF;
 IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app') THEN
   IF NOT has_table_privilege('controlplus_app', 'seguridad.permiso', 'SELECT')
      OR NOT has_table_privilege('controlplus_app', 'seguridad.rol_permiso', 'SELECT')
   THEN RAISE EXCEPTION 'BUSINESS_VAT_APP_PRIVILEGES_MISSING: SELECT sobre seguridad.permiso o seguridad.rol_permiso'; END IF;
   FOR column_name IN SELECT unnest(ARRAY['iva_negocio_habilitado', 'iva_negocio_porcentaje', 'iva_negocio_version', 'iva_negocio_actualizado_utc']) LOOP
     IF NOT has_column_privilege('controlplus_app', 'configuracion.establecimiento', column_name, 'SELECT')
        OR NOT has_column_privilege('controlplus_app', 'configuracion.establecimiento', column_name, 'UPDATE')
     THEN RAISE EXCEPTION 'BUSINESS_VAT_APP_PRIVILEGES_MISSING: SELECT/UPDATE en configuracion.establecimiento.%', column_name; END IF;
   END LOOP;
 END IF;
END $check$;
SELECT 'BUSINESS_VAT_SETTING_POSTFLIGHT_OK'; ROLLBACK;
'@ } else { @'
BEGIN READ ONLY;
DO $check$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='ventas.detalle_venta'::regclass AND conname='ck_detalle_venta_iva_incluido'
   AND position(', 4)' in pg_get_constraintdef(oid)) > 0
   AND position(', 0)' in pg_get_constraintdef(oid)) > 0)
 THEN RAISE EXCEPTION 'BUSINESS_VAT_SALES_POSTFLIGHT_REJECTED'; END IF;
END $check$;
SELECT 'BUSINESS_VAT_SALES_POSTFLIGHT_OK'; ROLLBACK;
'@ }
      $null = ReadSql "postflight-$i" $post
      $salesAfter = @(ReadSql "sales-after-$i" $salesSql | Where-Object { $_ -match '^(venta|detalle_venta)\|' })
      if (($salesAfter -join "`n") -cne ($salesBefore -join "`n")) { throw "Ventas existentes cambiaron en etapa $i." }
      Remove-Item Env:PGOPTIONS
    }
    $env:PGOPTIONS = '-c default_transaction_read_only=on'
    AssertHistory 21 $targets[1] 'final'
    Write-Host "BUSINESS_VAT_ACTIVATION_OK; evidencia: $evidence; respaldo SHA-256: $backupHash; API SHA-256: $binaryHash"
  } finally {
    Remove-Item Env:PGPASSWORD,Env:PGHOST,Env:PGPORT,Env:PGUSER,Env:PGDATABASE,Env:PGOPTIONS,Env:ConnectionStrings__ControlPlusDb,Env:CONTROLPLUS_MIGRATIONS_ENABLED -ErrorAction SilentlyContinue
  }
}
```

La comparación de ventas cubre **todas las filas y columnas** de `ventas.venta` y `ventas.detalle_venta`, ordenadas por UUID, con conteos y huellas antes del respaldo y tras cada migración. El segundo postflight exige ambos redondeos del CHECK y su forma exacta debe cotejarse con `20261110020000_BusinessVatWholePesoSalesV1.cs` en la evidencia antes de declarar cierre operativo. El primer postflight exige que `TAXES.MANAGE` exista una sola vez y se conceda **solo** a la plantilla Administrador; no afirma que no existan excepciones individuales. Si existe `controlplus_app`, también comprueba sus permisos de lectura y actualización. Si falta alguno, detenerse y evaluar una migración compensatoria; este bloque no concede permisos.

### Prueba automática obligatoria en base temporal

Antes del bloque real, ejecutar `BusinessVatWholePesoSalesMigrationTests.WholePesoCheck_RejectsBeforeMigration_AcceptsAfter_AndPreservesFourDecimalRows`. Usa `LocalPostgresDatabase`, que crea y elimina una base `controlplus_test_*`. Migra hasta `BusinessVatSettingV1`, exige que la consulta idéntica al postflight devuelva `false` y que un detalle con base 840 e IVA 160 sobre 1000 al 19 % sea rechazado. Inserta un detalle histórico con base 840.3361 e IVA 159.6639; después migra hasta `BusinessVatWholePesoSalesV1`, exige que la consulta devuelva `true`, que el detalle histórico permanezca y que el detalle de pesos enteros sea aceptado. No utiliza `controlplus`.

```powershell
& {
  $ErrorActionPreference = 'Stop'
  Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
  $previous = [Environment]::GetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', 'Process')
  $secret = $null; $credential = $null; $connection = $null
  try {
    $secret = Read-Host 'Contraseña del rol controlplus_test_admin' -AsSecureString
    $credential = [pscredential]::new('controlplus_test_admin', $secret)
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'; $connection['Port'] = 5432
    $connection['Database'] = 'postgres'; $connection['Username'] = 'controlplus_test_admin'
    $connection['Password'] = $credential.GetNetworkCredential().Password
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $connection.psbase.ConnectionString, 'Process')
    dotnet build .\ControlPlus.Infrastructure.Tests\ControlPlus.Infrastructure.Tests.csproj -c Release -m:1 /p:BuildInParallel=false /warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Falló build serial; no aplicar.' }
    $results = Join-Path $env:TEMP ('ControlPlus-BusinessVat-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $results | Out-Null
    dotnet test .\ControlPlus.Infrastructure.Tests\ControlPlus.Infrastructure.Tests.csproj -c Release `
      --no-build --no-restore -m:1 /p:BuildInParallel=false `
      --filter 'FullyQualifiedName~BusinessVatWholePesoSalesMigrationTests.WholePesoCheck_RejectsBeforeMigration_AcceptsAfter_AndPreservesFourDecimalRows' `
      --results-directory $results --logger 'trx;LogFileName=BusinessVatWholePeso.trx'
    if ($LASTEXITCODE -ne 0) { throw "Prueba temporal fallida; TRX: $results" }
    Write-Host "Prueba temporal aprobada; TRX: $results"
  } finally {
    [Environment]::SetEnvironmentVariable('CONTROLPLUS_TEST_ADMIN_CONNECTION', $previous, 'Process')
    if ($null -ne $connection) { $connection.Clear() }
    if ($null -ne $secret) { $secret.Dispose() }
    $credential = $null; $connection = $null; $secret = $null
  }
}
```

## Reinicio y comprobaciones HTTP

Tras `BUSINESS_VAT_ACTIVATION_OK`, el arranque desde el `api-bin` de la evidencia usa la configuración de Development/User Secrets habitual de la API y el rol `controlplus_app`. La gramática cerrada de la regla 9 de `AGENTS.md` rechaza `--urls`: el puerto se fija con `ASPNETCORE_URLS` y el entorno con `ASPNETCORE_ENVIRONMENT`. Ejemplo en otra consola de PowerShell: `$env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = 'http://localhost:5000'; dotnet %USERPROFILE%\ControlPlus-Backups\BusinessVat-20261111T140623Z-d62b0903500246c79110ba1fa64eb681\api-bin\ControlPlus.Api.dll`. La ejecución habitual del operador desde el repositorio es `dotnet run --project ControlPlus.Api --launch-profile http`. La comprobación HTTP y la venta de validación realizadas se registran al inicio de este documento.

Si cualquier etapa falla, conservar la API detenida y la carpeta de evidencia. Inspeccionar historial y objetos solo en lectura: una migración puede haber quedado aplicada. No repetir el bloque, no ejecutar `Down` ni restaurar el respaldo sobre la base real automáticamente. Ensayar cualquier recuperación en un clon nuevo antes de decidir sobre producción.

# Activación del segundo corte de IVA: apartados y cambios (historial 21→22)

**Pendiente de ejecución por el operador.** Objetivo único: `20261111010000_BusinessVatWholePesoLayawaysChangesV1`. El agente no lo aplicó ni consultó `controlplus`. Las dos migraciones del primer corte ya están aplicadas y este procedimiento no las repite.

La migración sustituye solo `ventas.ck_detalle_apartado_iva_incluido` y `ventas.ck_detalle_cambio_iva_incluido`. Ambos CHECK aceptan la base histórica redondeada a cuatro decimales o la nueva base redondeada a COP entero, siempre con `base_gravable + valor_iva = subtotal_neto`. Los CHECK de las cabeceras `ventas.apartado` y `ventas.cambio_venta` permanecen intactos. No hay `UPDATE`, `INSERT` ni `DELETE` de filas de negocio.

## Precondición en base temporal

Antes de abrir la ventana real, el operador debe ejecutar `BusinessVatWholePesoLayawaysChangesMigrationTests` y las pruebas focalizadas de `LayawayBusinessVatApiTests`, `SaleChangeBusinessVatApiTests` y `SalesOverview_ChangeReconcilesOriginalReturnedAndDeliveredHistoricalCosts` con `CONTROLPLUS_TEST_ADMIN_CONNECTION`. `LocalPostgresDatabase` debe crear exclusivamente bases `controlplus_test_*`. El resultado debe ser cero fallidas y cero omitidas.

## Bloque único de activación para el operador (segundo corte: ejecutado el 12 de noviembre de 2026; no volver a ejecutar)

**Activación ejecutada por el operador el 12 de noviembre de 2026**: `BUSINESS_VAT_LAYAWAYS_CHANGES_ACTIVATION_OK`, historial 21→22. Evidencia en `%USERPROFILE%\ControlPlus-Backups\BusinessVat-LayawaysChanges-20261112T140538Z-85af942f8ee645148f30f7084a8a2fc0`; respaldo SHA-256 `EBA10ACCB9E58A05BEAADF7F2E51482A4D5329E17A14E6A062371CA79ECC55E5`; API SHA-256 `D317110324379115741A9E7581CAB480D503B0794F97B564485D7D49628A3461`. El postflight y las huellas (cuatro tablas vacías, idénticas antes y después) quedaron conformes; la API respondió 200/200/200 y 401 en `/api/settings/vat` sin sesión. **No repetir ni ejecutar `Down`.** El bloque que sigue se conserva solo como registro.

Detener API, frontend y cualquier otro cliente antes de ejecutarlo. El bloque exige PostgreSQL 18, historial exacto **21**, última `20261110020000_BusinessVatWholePesoSalesV1`, ausencia del objetivo y ningún otro cliente conectado. Publica la API en serie con `/warnaserror`, crea un respaldo nuevo, conserva su SHA-256, valida el TOC y una lectura completa, repite historial y huellas, hace una sola confirmación `--migrate-to`, y ejecuta el postflight de solo lectura. Ejecutar desde una consola PowerShell abierta por el operador; la contraseña se mantiene solo en memoria.

```powershell
& {
  $ErrorActionPreference = 'Stop'
  Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
  $target = '20261111010000_BusinessVatWholePesoLayawaysChangesV1'
  $previous = '20261110020000_BusinessVatWholePesoSalesV1'
  $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
  $evidence = Join-Path $env:USERPROFILE "ControlPlus-Backups\BusinessVat-LayawaysChanges-$stamp-$([guid]::NewGuid().ToString('N'))"
  New-Item -ItemType Directory -Path $evidence -Force | Out-Null
  $publish = Join-Path $evidence 'api-bin'
  dotnet publish .\ControlPlus.Api\ControlPlus.Api.csproj -c Release -o $publish -m:1 /p:BuildInParallel=false /warnaserror
  if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación serial; detener antes de acceder a la base.' }
  $apiDll = Join-Path $publish 'ControlPlus.Api.dll'
  $binaryHash = (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash

  $password = Read-Host 'Contraseña de postgres' -AsSecureString
  $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
  try { $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
  $env:PGHOST = '127.0.0.1'; $env:PGPORT = '5432'; $env:PGUSER = 'postgres'; $env:PGDATABASE = 'controlplus'
  $env:PGOPTIONS = '-c default_transaction_read_only=on'
  $env:ConnectionStrings__ControlPlusDb = "Host=127.0.0.1;Port=5432;Database=controlplus;Username=postgres;Password=$($env:PGPASSWORD)"

  function Read-Sql([string]$name, [string]$sql) {
    $path = Join-Path $evidence "$name.sql"
    [IO.File]::WriteAllText($path, $sql, [Text.UTF8Encoding]::new($false))
    $output = & psql -X -v ON_ERROR_STOP=1 -A -t -f $path 2>&1
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $evidence "$name.txt") -Encoding UTF8
    if ($code -ne 0) { throw "Falló $name; conservar evidencia y no continuar." }
    return @($output)
  }

  $historySql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT count(*)::text || '|' || max("MigrationId") FROM public."__EFMigrationsHistory";
ROLLBACK;
'@
  $fingerprintsSql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT 'apartado|' || count(*) || '|' || coalesce(md5(string_agg(md5(row_to_json(t)::text), '' ORDER BY t.id::text)), md5('')) FROM ventas.apartado t;
SELECT 'detalle_apartado|' || count(*) || '|' || coalesce(md5(string_agg(md5(row_to_json(t)::text), '' ORDER BY t.id::text)), md5('')) FROM ventas.detalle_apartado t;
SELECT 'cambio_venta|' || count(*) || '|' || coalesce(md5(string_agg(md5(row_to_json(t)::text), '' ORDER BY t.id::text)), md5('')) FROM ventas.cambio_venta t;
SELECT 'detalle_cambio_venta|' || count(*) || '|' || coalesce(md5(string_agg(md5(row_to_json(t)::text), '' ORDER BY t.id::text)), md5('')) FROM ventas.detalle_cambio_venta t;
ROLLBACK;
'@
  function Assert-History([int]$count, [string]$last, [string]$stage) {
    $rows = @(Read-Sql "history-$stage" $historySql | Where-Object { $_ -match '^\d+\|' })
    if ($rows.Count -ne 1 -or $rows[0] -cne "$count|$last") { throw "Historial inesperado en $stage`: $rows" }
  }
  function Read-Fingerprints([string]$stage) {
    $rows = @(Read-Sql "fingerprints-$stage" $fingerprintsSql |
      Where-Object { $_ -match '^(apartado|detalle_apartado|cambio_venta|detalle_cambio_venta)\|' })
    if ($rows.Count -ne 4) { throw "Huellas incompletas en $stage." }
    return @($rows)
  }

  try {
    $preflightSql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $guard$ BEGIN
  IF current_database() <> 'controlplus'
     OR current_user <> 'postgres'
     OR current_setting('transaction_read_only') <> 'on'
     OR current_setting('server_version_num')::int NOT BETWEEN 180000 AND 189999
     OR (SELECT count(*) FROM public."__EFMigrationsHistory") <> 21
     OR (SELECT max("MigrationId") FROM public."__EFMigrationsHistory") <> '20261110020000_BusinessVatWholePesoSalesV1'
     OR EXISTS (SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261111010000_BusinessVatWholePesoLayawaysChangesV1')
     OR EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND backend_type = 'client backend')
     OR (SELECT count(*) FROM pg_constraint WHERE
          (conrelid = 'ventas.detalle_apartado'::regclass AND conname = 'ck_detalle_apartado_iva_incluido') OR
          (conrelid = 'ventas.detalle_cambio_venta'::regclass AND conname = 'ck_detalle_cambio_iva_incluido')) <> 2
  THEN RAISE EXCEPTION 'BUSINESS_VAT_LAYAWAYS_CHANGES_PREFLIGHT_REJECTED'; END IF;
END $guard$;
SELECT 'BUSINESS_VAT_LAYAWAYS_CHANGES_PREFLIGHT_OK';
ROLLBACK;
'@
    if (@(Read-Sql 'preflight' $preflightSql | Where-Object { $_ -eq 'BUSINESS_VAT_LAYAWAYS_CHANGES_PREFLIGHT_OK' }).Count -ne 1) {
      throw 'Preflight incompleto; detener.'
    }
    Assert-History 21 $previous 'before'
    $before = @(Read-Fingerprints 'before')

    $dump = Join-Path $evidence 'controlplus-before.dump'
    & pg_dump -Fc --no-owner --no-acl -f $dump controlplus
    if ($LASTEXITCODE -ne 0) { throw 'Falló pg_dump; detener.' }
    $backupHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $backupHash | Set-Content -LiteralPath (Join-Path $evidence 'backup.sha256') -Encoding ASCII
    & pg_restore --list $dump > (Join-Path $evidence 'backup.toc.txt')
    if ($LASTEXITCODE -ne 0) { throw 'El TOC del respaldo no es legible.' }
    & pg_restore --file NUL $dump
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) {
      throw 'Falló la verificación integral del respaldo.'
    }
    Assert-History 21 $previous 'after-backup'
    $afterBackup = @(Read-Fingerprints 'after-backup')
    if (($afterBackup -join "`n") -cne ($before -join "`n")) { throw 'Las filas cambiaron durante el respaldo.' }
    if ((Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $binaryHash) { throw 'Cambió el binario congelado.' }

    Remove-Item Env:PGOPTIONS
    $env:CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
    $applyOutput = & dotnet $apiDll --migrate-to $target 2>&1
    $applyCode = $LASTEXITCODE
    $applyOutput | Set-Content -LiteralPath (Join-Path $evidence "apply-$target.txt") -Encoding UTF8
    if ($applyCode -ne 0) { throw 'El migrador falló; no reintentar automáticamente.' }

    $env:PGOPTIONS = '-c default_transaction_read_only=on'
    Assert-History 22 $target 'postflight'
    $postflightSql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $guard$ BEGIN
  IF (SELECT count(*) FROM pg_constraint
      WHERE ((conrelid = 'ventas.detalle_apartado'::regclass AND conname = 'ck_detalle_apartado_iva_incluido')
          OR (conrelid = 'ventas.detalle_cambio_venta'::regclass AND conname = 'ck_detalle_cambio_iva_incluido'))
        AND position(', 4)' IN pg_get_constraintdef(oid)) > 0
        AND position(', 0)' IN pg_get_constraintdef(oid)) > 0
        -- PostgreSQL devuelve la definición con paréntesis, p. ej. ((base_gravable + valor_iva) = subtotal_neto):
        -- se compara sin paréntesis ni espacios para que coincida con cualquier forma normalizada.
        AND position('base_gravable+valor_iva=subtotal_neto' IN regexp_replace(pg_get_constraintdef(oid), '[() ]', '', 'g')) > 0) <> 2
  THEN RAISE EXCEPTION 'BUSINESS_VAT_LAYAWAYS_CHANGES_POSTFLIGHT_REJECTED'; END IF;
END $guard$;
SELECT conrelid::regclass, conname, pg_get_constraintdef(oid)
FROM pg_constraint
WHERE (conrelid = 'ventas.detalle_apartado'::regclass AND conname = 'ck_detalle_apartado_iva_incluido')
   OR (conrelid = 'ventas.detalle_cambio_venta'::regclass AND conname = 'ck_detalle_cambio_iva_incluido')
ORDER BY conrelid::regclass::text;
SELECT 'BUSINESS_VAT_LAYAWAYS_CHANGES_POSTFLIGHT_OK';
ROLLBACK;
'@
    if (@(Read-Sql 'postflight' $postflightSql | Where-Object { $_ -eq 'BUSINESS_VAT_LAYAWAYS_CHANGES_POSTFLIGHT_OK' }).Count -ne 1) {
      throw 'Postflight incompleto; no iniciar la API.'
    }
    $after = @(Read-Fingerprints 'after')
    if (($after -join "`n") -cne ($before -join "`n")) { throw 'Las huellas de apartados o cambios no se conservaron.' }
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash -or
        (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $binaryHash) {
      throw 'Cambió el respaldo o el binario después de aplicar.'
    }
    Write-Host "BUSINESS_VAT_LAYAWAYS_CHANGES_ACTIVATION_OK; evidencia: $evidence; respaldo SHA-256: $backupHash; API SHA-256: $binaryHash"
  } finally {
    Remove-Item Env:PGPASSWORD,Env:PGHOST,Env:PGPORT,Env:PGUSER,Env:PGDATABASE,Env:PGOPTIONS,Env:ConnectionStrings__ControlPlusDb,Env:CONTROLPLUS_MIGRATIONS_ENABLED -ErrorAction SilentlyContinue
    if ($null -ne $password) { $password.Dispose() }
    $password = $null
  }
}
```

La única escritura autorizada por el bloque es la confirmación del objetivo exacto mediante el migrador. Si cualquier etapa falla, conservar la API detenida y la carpeta de evidencia; no repetir, no ejecutar `Down` y no restaurar automáticamente el respaldo sobre la base real. Primero diagnosticar en lectura y ensayar cualquier recuperación en un clon nuevo.

# Activación conjunta de reglas de apartados y créditos (historial 22→23)

**Activada por el operador el 12 de noviembre de 2026 (`LAYAWAY_CREDIT_RULES_ACTIVATION_OK`; no repetir).** Objetivo único: `20261112010000_LayawayMinimumInitialPaymentV1`. Incluye el interruptor del pago inicial porcentual de apartados y el plazo predeterminado de créditos. El agente no consultó `controlplus` ni aplicó esta migración. La línea base obligatoria es de 22 migraciones, con `20261111010000_BusinessVatWholePesoLayawaysChangesV1` como última; esa migración ya fue aplicada una vez y no se repite.

Antes de abrir la ventana real deben aprobar, con `CONTROLPLUS_TEST_ADMIN_CONNECTION` y exclusivamente sobre bases `controlplus_test_*`, `LayawayMinimumInitialPaymentMigrationTests`, `LayawaySettingsApiTests`, `LayawayCreateApiFlowTests`, `CreditSettingsApiTests` y `CreditSalesApiTests`, sin fallidas ni omitidas. También debe aprobar la compilación serial de la solución con `/warnaserror`. La API y el frontend deben estar detenidos y no puede existir otro cliente de base. Este bloque vuelve a compilar y congela el binario exacto que aplica.

## Bloque único para el operador (ejecutado el 12 de noviembre de 2026; no repetir)

```powershell
& {
  $ErrorActionPreference = 'Stop'
  Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
  $target = '20261112010000_LayawayMinimumInitialPaymentV1'
  $previous = '20261111010000_BusinessVatWholePesoLayawaysChangesV1'
  $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
  $evidence = Join-Path $env:USERPROFILE "ControlPlus-Backups\LayawayCreditRules-$stamp-$([guid]::NewGuid().ToString('N'))"
  New-Item -ItemType Directory -Path $evidence -Force | Out-Null
  $publish = Join-Path $evidence 'api-bin'
  dotnet publish .\ControlPlus.Api\ControlPlus.Api.csproj -c Release -o $publish -m:1 /p:BuildInParallel=false /warnaserror
  if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación; detener.' }
  $apiDll = Join-Path $publish 'ControlPlus.Api.dll'
  $binaryHash = (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash

  $password = Read-Host 'Contraseña de postgres' -AsSecureString
  $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
  try { $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
  $env:PGHOST = '127.0.0.1'
  $env:PGPORT = '5432'
  $env:PGUSER = 'postgres'
  $env:PGDATABASE = 'controlplus'
  $env:PGOPTIONS = '-c default_transaction_read_only=on'
  $env:ConnectionStrings__ControlPlusDb = "Host=127.0.0.1;Port=5432;Database=controlplus;Username=postgres;Password=$($env:PGPASSWORD)"

  function Read-Sql([string]$name, [string]$sql) {
    $path = Join-Path $evidence "$name.sql"
    [IO.File]::WriteAllText($path, $sql, [Text.UTF8Encoding]::new($false))
    $out = & psql -X -v ON_ERROR_STOP=1 -A -t -f $path 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Falló $name`: $out" }
    $out | Set-Content -LiteralPath (Join-Path $evidence "$name.txt") -Encoding UTF8
    return @($out)
  }

  $historySql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT count(*)::text || '|' || max("MigrationId") FROM public."__EFMigrationsHistory";
ROLLBACK;
'@
  function Assert-History([int]$count, [string]$last, [string]$stage) {
    $rows = @(Read-Sql "history-$stage" $historySql | Where-Object { $_ -match '^\d+\|' })
    if ($rows.Count -ne 1 -or $rows[0] -cne "$count|$last") {
      throw "Historial inesperado en $stage`: $rows"
    }
  }

  $fingerprintSql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT 'establecimiento|' || count(*) || '|' ||
  coalesce(md5(string_agg(md5(((to_jsonb(e) - 'apartado_exige_minimo') - 'dias_credito_predeterminado')::text), '' ORDER BY e.id::text)), md5(''))
FROM configuracion.establecimiento e;
SELECT 'apartado|' || count(*) || '|' ||
  coalesce(md5(string_agg(md5(row_to_json(a)::text), '' ORDER BY a.id::text)), md5(''))
FROM ventas.apartado a;
SELECT 'credito|' || count(*) || '|' ||
  coalesce(md5(string_agg(md5(row_to_json(c)::text), '' ORDER BY c.id::text)), md5(''))
FROM ventas.credito c;
ROLLBACK;
'@
  function Read-Fingerprints([string]$stage) {
    return @(Read-Sql "fingerprints-$stage" $fingerprintSql |
      Where-Object { $_ -match '^(establecimiento|apartado|credito)\|' })
  }

  try {
    $preflightSql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $guard$ BEGIN
  IF current_database() <> 'controlplus'
     OR current_user <> 'postgres'
     OR current_setting('transaction_read_only') <> 'on'
     OR current_setting('server_version_num')::int NOT BETWEEN 180000 AND 189999
     OR (SELECT count(*) FROM public."__EFMigrationsHistory") <> 22
     OR (SELECT max("MigrationId") FROM public."__EFMigrationsHistory") <> '20261111010000_BusinessVatWholePesoLayawaysChangesV1'
     OR EXISTS (SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261112010000_LayawayMinimumInitialPaymentV1')
     OR EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend')
     OR EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema='configuracion' AND table_name='establecimiento'
                  AND column_name IN ('apartado_exige_minimo','dias_credito_predeterminado'))
     OR EXISTS (SELECT 1 FROM pg_constraint
                WHERE conrelid='configuracion.establecimiento'::regclass
                  AND conname='ck_establecimiento_dias_credito_predeterminado')
     OR EXISTS (SELECT 1 FROM ventas.apartado
                WHERE porcentaje_minimo_inicial > 0
                  AND abono_inicial < ceil(total * porcentaje_minimo_inicial / 100))
     OR (SELECT count(*) FROM pg_constraint
         WHERE conrelid='ventas.apartado'::regclass AND conname='ck_apartado_montos') <> 1
  THEN RAISE EXCEPTION 'LAYAWAY_CREDIT_RULES_PREFLIGHT_REJECTED'; END IF;
END $guard$;
SELECT 'LAYAWAY_CREDIT_RULES_PREFLIGHT_OK';
ROLLBACK;
'@
    if (@(Read-Sql 'preflight' $preflightSql |
        Where-Object { $_ -eq 'LAYAWAY_CREDIT_RULES_PREFLIGHT_OK' }).Count -ne 1) {
      throw 'Preflight incompleto.'
    }
    Assert-History 22 $previous 'before'
    $before = @(Read-Fingerprints 'before')
    if ($before.Count -ne 3) { throw 'Huellas iniciales incompletas.' }

    $dump = Join-Path $evidence 'controlplus-before.dump'
    & pg_dump -Fc --no-owner --no-acl -f $dump controlplus
    if ($LASTEXITCODE -ne 0) { throw 'Falló pg_dump.' }
    $backupHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $backupHash | Set-Content -LiteralPath (Join-Path $evidence 'backup.sha256') -Encoding ASCII
    & pg_restore --list $dump > (Join-Path $evidence 'backup.toc.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Respaldo sin TOC legible.' }
    & pg_restore --file NUL $dump
    if ($LASTEXITCODE -ne 0 -or
        (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) {
      throw 'Falló la verificación del respaldo.'
    }
    Assert-History 22 $previous 'after-backup'
    $afterBackup = @(Read-Fingerprints 'after-backup')
    if (($afterBackup -join "`n") -cne ($before -join "`n")) {
      throw 'Los datos cambiaron durante el respaldo.'
    }

    if ((Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $binaryHash -or
        (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) {
      throw 'Cambió el binario o el respaldo antes de aplicar.'
    }
    Remove-Item Env:PGOPTIONS
    $env:CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
    $applyOutput = & dotnet $apiDll --migrate-to $target 2>&1
    $applyCode = $LASTEXITCODE
    $applyOutput | Set-Content -LiteralPath (Join-Path $evidence "apply-$target.txt") -Encoding UTF8
    if ($applyCode -ne 0) { throw 'El migrador falló; no reintentar automáticamente.' }

    $env:PGOPTIONS = '-c default_transaction_read_only=on'
    Assert-History 23 $target 'postflight'
    $postflightSql = @'
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $guard$ DECLARE credit_check text; layaway_check text; BEGIN
  SELECT pg_get_constraintdef(oid) INTO credit_check
  FROM pg_constraint
  WHERE conrelid='configuracion.establecimiento'::regclass
    AND conname='ck_establecimiento_dias_credito_predeterminado';
  SELECT pg_get_constraintdef(oid) INTO layaway_check
  FROM pg_constraint
  WHERE conrelid='ventas.apartado'::regclass AND conname='ck_apartado_montos';
  IF (SELECT count(*) FROM information_schema.columns
      WHERE table_schema='configuracion' AND table_name='establecimiento'
        AND ((column_name='apartado_exige_minimo' AND data_type='boolean' AND is_nullable='NO' AND column_default='true')
          OR (column_name='dias_credito_predeterminado' AND data_type='integer' AND is_nullable='YES' AND column_default='30'))) <> 2
     OR credit_check IS NULL
     OR position('dias_credito_predeterminadoISNULL' IN regexp_replace(credit_check, '[() ]', '', 'g')) = 0
     OR position('dias_credito_predeterminado>=1' IN regexp_replace(credit_check, '[() ]', '', 'g')) = 0
     OR position('dias_credito_predeterminado<=90' IN regexp_replace(credit_check, '[() ]', '', 'g')) = 0
     OR layaway_check IS NULL
     OR position('porcentaje_minimo_inicial>=0' IN regexp_replace(layaway_check, '[() ]', '', 'g')) = 0
     OR position('CASE' IN layaway_check) = 0
     OR position('THEN1' IN regexp_replace(layaway_check, '[() ]', '', 'g')) = 0
     OR position('ceil' IN lower(layaway_check)) = 0
     OR EXISTS (SELECT 1 FROM configuracion.establecimiento
                WHERE apartado_exige_minimo IS DISTINCT FROM true
                   OR dias_credito_predeterminado IS DISTINCT FROM 30)
     OR (EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app')
         AND NOT has_table_privilege('controlplus_app','configuracion.establecimiento','SELECT,UPDATE'))
  THEN RAISE EXCEPTION 'LAYAWAY_CREDIT_RULES_POSTFLIGHT_REJECTED'; END IF;
END $guard$;
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema='configuracion' AND table_name='establecimiento'
  AND column_name IN ('apartado_exige_minimo','dias_credito_predeterminado')
ORDER BY column_name;
SELECT conrelid::regclass, conname, pg_get_constraintdef(oid)
FROM pg_constraint
WHERE (conrelid='configuracion.establecimiento'::regclass
       AND conname='ck_establecimiento_dias_credito_predeterminado')
   OR (conrelid='ventas.apartado'::regclass AND conname='ck_apartado_montos')
ORDER BY conrelid::regclass::text, conname;
SELECT 'LAYAWAY_CREDIT_RULES_POSTFLIGHT_OK';
ROLLBACK;
'@
    if (@(Read-Sql 'postflight' $postflightSql |
        Where-Object { $_ -eq 'LAYAWAY_CREDIT_RULES_POSTFLIGHT_OK' }).Count -ne 1) {
      throw 'Postflight incompleto; no iniciar la API.'
    }
    $after = @(Read-Fingerprints 'after')
    if (($after -join "`n") -cne ($before -join "`n")) {
      throw 'Las huellas históricas no se conservaron.'
    }
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash -or
        (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $binaryHash) {
      throw 'Cambió el respaldo o el binario después de aplicar.'
    }
    $fingerprintHash = (Get-FileHash -LiteralPath (Join-Path $evidence 'fingerprints-before.txt') -Algorithm SHA256).Hash
    $result = [ordered]@{
      result = 'LAYAWAY_CREDIT_RULES_ACTIVATION_OK'
      target = $target
      historyBefore = 22
      historyAfter = 23
      backupSha256 = $backupHash
      apiSha256 = $binaryHash
      fingerprintsSha256 = $fingerprintHash
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "LAYAWAY_CREDIT_RULES_ACTIVATION_OK; evidencia: $evidence; respaldo SHA-256: $backupHash; API SHA-256: $binaryHash; huellas SHA-256: $fingerprintHash"
  } finally {
    Remove-Item Env:PGPASSWORD,Env:PGHOST,Env:PGPORT,Env:PGUSER,Env:PGDATABASE,Env:PGOPTIONS,Env:ConnectionStrings__ControlPlusDb,Env:CONTROLPLUS_MIGRATIONS_ENABLED -ErrorAction SilentlyContinue
    if ($null -ne $password) { $password.Dispose() }
    $password = $null
  }
}
```

La única escritura autorizada por este bloque es la migración objetivo exacta. Si cualquier etapa falla, conservar la API detenida y la evidencia; no repetir, no ejecutar `Down` y no restaurar automáticamente sobre `controlplus`. Diagnosticar en solo lectura y ensayar cualquier corrección en un clon nuevo.

## Evidencia de la activación (12 de noviembre de 2026)

Comunicada por el operador: `LAYAWAY_CREDIT_RULES_ACTIVATION_OK` en `%USERPROFILE%\ControlPlus-Backups\LayawayCreditRules-20261112T162110Z-bd78f806dab4423e8c240678c13c5673`. El bloque terminó sin errores, por lo que el preflight (historial 22, última `20261111010000_BusinessVatWholePesoLayawaysChangesV1`, sin otros clientes), el respaldo verificado, el migrador, el postflight (columnas, CHECK 1–90, `ck_apartado_montos` y permisos de `controlplus_app`) y la igualdad de huellas de `establecimiento` (sin las dos columnas nuevas), `apartado` y `credito` resultaron conformes. Historial 22→23.

- Respaldo previo SHA-256: `9940859DBC82C50E025D84DBF6128DCB04DF31BCDE0087E8B402E02D6FA6B856`.
- API congelada SHA-256: `BD9D6E497645A8682380A8235968B6FCB099A623C9B22BC4E10F8D1F54D1EBD6`.
- Huellas de conservación SHA-256: `7B308F791578B2C7034479FBDB2C316E89C74B5E2755D81836314B6C264F8CB9`.
- Tras reiniciar la API: HTTP 200 en `/api/health`, `/api/health/database` y `/openapi/v1.json`; 401 sin sesión en `/api/credits/settings`, `/api/sales/credit-rules` y `/api/layaways/creation-rules`.
- Revisión visual aprobada por el operador: Configuración → Créditos y Apartados, y fecha de vencimiento propuesta en la venta a crédito.

**No repetir la migración ni ejecutar `Down`.** El agente no accedió a `controlplus`.
