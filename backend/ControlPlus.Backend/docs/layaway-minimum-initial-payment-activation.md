# Documento histórico sustituido — no ejecutar

**El bloque de este archivo quedó obsoleto al autorizar el usuario, el 12 de noviembre de 2026, la activación conjunta de apartados y créditos. No ejecutar ningún bloque inferior.** El único procedimiento vigente es [business-vat-activation.md](business-vat-activation.md#activación-conjunta-de-reglas-de-apartados-y-créditos-historial-2223), para la migración única `20261112010000_LayawayMinimumInitialPaymentV1` y el salto 22→23.

<!-- Contenido histórico oculto deliberadamente: no ejecutar.
# Activación del mínimo inicial configurable de apartados (historial 22→23; obsoleta)

**Pendiente de ejecución por el operador.** Objetivo único:
`20261112010000_LayawayMinimumInitialPaymentV1`. El agente no consultó ni
modificó `controlplus` y no aplicó la migración a una base persistente.

**Bloqueado antes de activación:** durante la implementación apareció trabajo
concurrente no solicitado que añadió `dias_credito_predeterminado` al mismo
archivo de migración. El bloque inferior se detiene antes de publicar o acceder
a PostgreSQL mientras ese cambio siga presente. El usuario debe decidir si
autoriza una activación conjunta o si ambos cambios se separan y se renumera el
historial; no ejecutar el objetivo actual hasta resolverlo.

La migración añade `configuracion.establecimiento.apartado_exige_minimo` como
`boolean NOT NULL DEFAULT true` y reemplaza únicamente
`ventas.ck_apartado_montos`. No actualiza, inserta ni elimina filas de negocio.
Las filas históricas conservan su porcentaje y abono. Los apartados nuevos que
se creen con la exigencia apagada guardan `porcentaje_minimo_inicial = 0` y
siguen exigiendo `abono_inicial >= 1`.

## Precondición obligatoria en base temporal

Antes de abrir la ventana real, ejecutar con `CONTROLPLUS_TEST_ADMIN_CONNECTION`
la prueba
`LayawayMinimumInitialPaymentMigrationTests.Migration_DefaultsRequirementOn_PreservesHistory_AndAcceptsOnePesoSnapshot`
y las clases `LayawaySettingsApiTests` y `LayawayCreateApiFlowTests`. El fixture
debe crear exclusivamente bases `controlplus_test_*`; el resultado debe tener
cero fallidas y cero omitidas. Esta validación está pendiente mientras no se
proporcione la credencial del rol local `controlplus_test_admin`.

## Bloque único para el operador

Detener API, frontend y cualquier otro cliente antes de ejecutarlo. El bloque
exige PostgreSQL 18, historial exacto **22**, última migración
`20261111010000_BusinessVatWholePesoLayawaysChangesV1`, ausencia del objetivo y
ningún otro cliente conectado. Publica la API en serie con `/warnaserror`, crea
un respaldo nuevo, guarda su SHA-256, valida el TOC y la lectura completa,
compara huellas antes y después, aplica una sola vez el objetivo exacto y hace
el postflight de solo lectura.

Ejecutar desde una consola PowerShell abierta por el operador. La contraseña se
mantiene solo en memoria. No ejecutar si la prueba temporal anterior no está
aprobada.

```powershell
& {
  $ErrorActionPreference = 'Stop'
  Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
  $migrationSource = '.\ControlPlus.Infrastructure\Persistence\Migrations\20261112010000_LayawayMinimumInitialPaymentV1.cs'
  if (Select-String -LiteralPath $migrationSource -SimpleMatch 'dias_credito_predeterminado' -Quiet) {
    throw 'Activación bloqueada: la migración contiene el cambio concurrente de plazo de crédito no conciliado.'
  }
  $target = '20261112010000_LayawayMinimumInitialPaymentV1'
  $previous = '20261111010000_BusinessVatWholePesoLayawaysChangesV1'
  $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
  $evidence = Join-Path $env:USERPROFILE "ControlPlus-Backups\LayawayMinimumInitial-$stamp-$([guid]::NewGuid().ToString('N'))"
  New-Item -ItemType Directory -Path $evidence -Force | Out-Null
  $publish = Join-Path $evidence 'api-bin'

  dotnet publish .\ControlPlus.Api\ControlPlus.Api.csproj -c Release -o $publish `
    -m:1 /p:BuildInParallel=false /warnaserror
  if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación serial; detener antes de acceder a la base.' }
  $apiDll = Join-Path $publish 'ControlPlus.Api.dll'
  $binaryHash = (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash
  $binaryHash | Set-Content -LiteralPath (Join-Path $evidence 'api.sha256') -Encoding ASCII

  $password = Read-Host 'Contraseña de postgres' -AsSecureString
  $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
  try { $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
  $env:PGHOST = '127.0.0.1'
  $env:PGPORT = '5432'
  $env:PGUSER = 'postgres'
  $env:PGDATABASE = 'controlplus'
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
SELECT 'establecimiento|' || count(*) || '|' ||
  coalesce(md5(string_agg(md5((to_jsonb(t) - 'apartado_exige_minimo')::text), '' ORDER BY t.id::text)), md5(''))
FROM configuracion.establecimiento t;
SELECT 'apartado|' || count(*) || '|' ||
  coalesce(md5(string_agg(md5(row_to_json(t)::text), '' ORDER BY t.id::text)), md5(''))
FROM ventas.apartado t;
ROLLBACK;
'@
  function Assert-History([int]$count, [string]$last, [string]$stage) {
    $rows = @(Read-Sql "history-$stage" $historySql | Where-Object { $_ -match '^\d+\|' })
    if ($rows.Count -ne 1 -or $rows[0] -cne "$count|$last") {
      throw "Historial inesperado en $stage`: $rows"
    }
  }
  function Read-Fingerprints([string]$stage) {
    $rows = @(Read-Sql "fingerprints-$stage" $fingerprintsSql |
      Where-Object { $_ -match '^(establecimiento|apartado)\|' })
    if ($rows.Count -ne 2) { throw "Huellas incompletas en $stage." }
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
     OR (SELECT count(*) FROM public."__EFMigrationsHistory") <> 22
     OR (SELECT max("MigrationId") FROM public."__EFMigrationsHistory") <> '20261111010000_BusinessVatWholePesoLayawaysChangesV1'
     OR EXISTS (SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261112010000_LayawayMinimumInitialPaymentV1')
     OR EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
                AND pid <> pg_backend_pid() AND backend_type = 'client backend')
     OR EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema='configuracion' AND table_name='establecimiento'
                  AND column_name='apartado_exige_minimo')
     OR EXISTS (SELECT 1 FROM ventas.apartado
                WHERE porcentaje_minimo_inicial > 0
                  AND abono_inicial < ceil(total * porcentaje_minimo_inicial / 100))
     OR (SELECT count(*) FROM pg_constraint
         WHERE conrelid='ventas.apartado'::regclass AND conname='ck_apartado_montos') <> 1
  THEN RAISE EXCEPTION 'LAYAWAY_MINIMUM_INITIAL_PREFLIGHT_REJECTED'; END IF;
END $guard$;
SELECT 'LAYAWAY_MINIMUM_INITIAL_PREFLIGHT_OK';
ROLLBACK;
'@
    if (@(Read-Sql 'preflight' $preflightSql |
        Where-Object { $_ -eq 'LAYAWAY_MINIMUM_INITIAL_PREFLIGHT_OK' }).Count -ne 1) {
      throw 'Preflight incompleto; detener.'
    }
    Assert-History 22 $previous 'before'
    $before = @(Read-Fingerprints 'before')

    $dump = Join-Path $evidence 'controlplus-before.dump'
    & pg_dump -Fc --no-owner --no-acl -f $dump controlplus
    if ($LASTEXITCODE -ne 0) { throw 'Falló pg_dump; detener.' }
    $backupHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
    $backupHash | Set-Content -LiteralPath (Join-Path $evidence 'backup.sha256') -Encoding ASCII
    & pg_restore --list $dump > (Join-Path $evidence 'backup.toc.txt')
    if ($LASTEXITCODE -ne 0) { throw 'El TOC del respaldo no es legible.' }
    & pg_restore --file NUL $dump
    if ($LASTEXITCODE -ne 0 -or
        (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash) {
      throw 'Falló la verificación integral del respaldo.'
    }
    Assert-History 22 $previous 'after-backup'
    $afterBackup = @(Read-Fingerprints 'after-backup')
    if (($afterBackup -join "`n") -cne ($before -join "`n")) {
      throw 'Las filas cambiaron durante el respaldo.'
    }
    if ((Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $binaryHash) {
      throw 'Cambió el binario congelado.'
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
DO $guard$ DECLARE v_check text; BEGIN
  SELECT lower(pg_get_constraintdef(oid)) INTO v_check
  FROM pg_constraint
  WHERE conrelid='ventas.apartado'::regclass AND conname='ck_apartado_montos';

  IF (SELECT count(*) FROM information_schema.columns
      WHERE table_schema='configuracion' AND table_name='establecimiento'
        AND column_name='apartado_exige_minimo' AND data_type='boolean'
        AND is_nullable='NO' AND column_default='true') <> 1
     OR EXISTS (SELECT 1 FROM configuracion.establecimiento WHERE NOT apartado_exige_minimo)
     OR v_check IS NULL
     OR position('porcentaje_minimo_inicial >= (0)::numeric' IN v_check) = 0
     OR position('case' IN v_check) = 0
     OR position('ceil' IN v_check) = 0
     OR position('abono_inicial >=' IN v_check) = 0
     OR position('when' IN v_check) = 0
     OR position('then (1)::numeric' IN v_check) = 0
  THEN RAISE EXCEPTION 'LAYAWAY_MINIMUM_INITIAL_POSTFLIGHT_REJECTED'; END IF;
END $guard$;
SELECT column_name,data_type,is_nullable,column_default
FROM information_schema.columns
WHERE table_schema='configuracion' AND table_name='establecimiento'
  AND column_name='apartado_exige_minimo';
SELECT conname,pg_get_constraintdef(oid)
FROM pg_constraint
WHERE conrelid='ventas.apartado'::regclass AND conname='ck_apartado_montos';
SELECT 'LAYAWAY_MINIMUM_INITIAL_POSTFLIGHT_OK';
ROLLBACK;
'@
    if (@(Read-Sql 'postflight' $postflightSql |
        Where-Object { $_ -eq 'LAYAWAY_MINIMUM_INITIAL_POSTFLIGHT_OK' }).Count -ne 1) {
      throw 'Postflight incompleto; no iniciar la API.'
    }
    $after = @(Read-Fingerprints 'after')
    if (($after -join "`n") -cne ($before -join "`n")) {
      throw 'Las huellas de establecimiento o apartado no se conservaron.'
    }
    if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $backupHash -or
        (Get-FileHash -LiteralPath $apiDll -Algorithm SHA256).Hash -cne $binaryHash) {
      throw 'Cambió el respaldo o el binario después de aplicar.'
    }
    Write-Host "LAYAWAY_MINIMUM_INITIAL_ACTIVATION_OK; evidencia: $evidence; respaldo SHA-256: $backupHash; API SHA-256: $binaryHash"
  }
  finally {
    Remove-Item Env:PGPASSWORD,Env:PGHOST,Env:PGPORT,Env:PGUSER,Env:PGDATABASE,Env:PGOPTIONS,Env:ConnectionStrings__ControlPlusDb,Env:CONTROLPLUS_MIGRATIONS_ENABLED -ErrorAction SilentlyContinue
    if ($null -ne $password) { $password.Dispose() }
    $password = $null
  }
}
```

La única escritura autorizada por el bloque es la confirmación del objetivo
exacto mediante el migrador. Si cualquier etapa falla, conservar la API
detenida y la carpeta de evidencia; no repetir, no ejecutar `Down` y no
restaurar automáticamente el respaldo sobre la base real. Diagnosticar primero
en lectura y ensayar cualquier recuperación en un clon nuevo.

## Reinicio y comprobaciones HTTP

Solo después de `LAYAWAY_MINIMUM_INITIAL_ACTIVATION_OK`, iniciar la API desde el
`api-bin` congelado. Confirmar 200 en `/api/health`, `/api/health/database` y
`/openapi/v1.json`; confirmar 401 sin sesión en
`/api/layaways/creation-rules`. Con sesiones de prueba, confirmar que el
Administrador puede leer y modificar `/api/layaways/settings`, que Supervisor y
Cajero reciben 403 allí, y que un usuario con `LAYAWAY.MANAGE` puede leer
`/api/layaways/creation-rules`. No crear un apartado real como parte del
postflight de esquema.
-->
