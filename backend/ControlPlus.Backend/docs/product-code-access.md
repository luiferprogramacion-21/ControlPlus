# Acceso del rol de la API a códigos de producto

Estado: `20261102010000_GrantProductCodeAccessV1` aplicada una vez a `controlplus` por el operador el 2 de noviembre de 2026. **No repetir.**
La migración publicada `20261029010000_PermanentProductReferencesV1` no se modifica ni se repite.

El operador informó respaldo verificado `%USERPROFILE%\ControlPlus-Backups\ProductCodeGrant-bef2a240c5954cb7afc7a0627bd70fd6\controlplus-before-grant.dump`, SHA-256 `78670E1FE27CA66BDB727144499CF1903BB9BB89E4B742EFDD930F8FD8DF60B9`, migrador con `Migration target '20261102010000_GrantProductCodeAccessV1' applied successfully.` y marcador final `PRODUCT_CODE_GRANT_OK`. El marcador exige preflight y postflight conformes en el bloque aprobado. Después, el operador informó `LISTADO_GET_OK status=200 total=2`, `QA_GET_OK status=200 total=1`, `REFERENCIA_GET_OK status=200 total=1` y `PRODUCT_CODE_RUNTIME_GET_OK` con la API operada como `controlplus_app`. No se probó alta/edición real.

La evidencia operativa indica GET sin búsqueda 200 y búsquedas 500. `producto_codigo`
pertenece a `postgres`; `controlplus_app` carece de SELECT, INSERT y UPDATE.
Las cuatro funciones creadas por la migración son `SECURITY INVOKER` (no declaran
`SECURITY DEFINER`). `siguiente_referencia_producto` llama `nextval`, lee la tabla;
`reservar_codigo_producto` inserta y lee la tabla; los triggers llaman a esta
última. Por ello el mínimo efectivo es SELECT/INSERT en `producto_codigo`, USAGE
en `producto_referencia_seq` y EXECUTE en las funciones. UPDATE/DELETE de la tabla
y SELECT/UPDATE de la secuencia no son necesarios. No hay otras tablas de alias
o historial en esa migración. La evolución previa de orden de inventario usa
`SECURITY DEFINER` y revoca acceso público; no sirve para este caso.

La migración protege la ausencia del rol en bases temporales. No crea roles ni
altera datos. Su Down revoca exclusivamente los tipos de permiso declarados en
Up; no está aprobado revertirla en la base persistente.

La prueba `ProductCodeAccessDatabaseTests` aplica el historial en una base
`controlplus_test_<UUID>` desechable. En Windows se ejecuta solo esa prueba con
el rol `controlplus_test_admin` y `CONTROLPLUS_TEST_ADMIN_CONNECTION`, siguiendo
la entrada segura del script `Run-LocalTests.ps1`; no ejecutar ese script completo
para este incidente, porque repite suites ya aprobadas. La prueba comprueba los
ACL tras aplicar la migración si existe `controlplus_app` en el clúster. El rol
de pruebas no puede crear ni asumir otro rol: una reproducción HTTP de búsqueda
y alta antes/después sigue pendiente de un clúster aislado con rol restringido.
Antes de compilar en Windows, ejecutar `dotnet restore ControlPlus.Backend.slnx
--force -m:1 /p:BuildInParallel=false` desde PowerShell. Un restore hecho desde
Linux deja rutas Linux en `obj/project.assets.json`; `--no-restore` en Windows
reutiliza esas rutas y falla con NETSDK1064. Después se puede compilar con
`--no-restore` y ejecutar únicamente la focalizada con `--no-build`.

## Bloque histórico ejecutado; no repetir

Este bloque se ejecutó en Windows PowerShell 5.1 durante una ventana sin API ni
otros escritores. El bloque crea un respaldo nuevo en formato custom, comprueba
que puede leerse por completo, ejecuta preflight/postflight de solo lectura y
aplica el objetivo exacto con el migrador separado. La comprobación del archivo
no equivale a un ensayo de restauración; realizarlo si el procedimiento local lo
exige. Se solicita la contraseña de `postgres` sin mostrarla y se limpia del
entorno al terminar. No copiar secretos a este documento.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
$target = '20261102010000_GrantProductCodeAccessV1'
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$apiDll = Join-Path $repo 'ControlPlus.Api\bin\Debug\net10.0\ControlPlus.Api.dll'
$backupDir = Join-Path $env:USERPROFILE ('ControlPlus-Backups\ProductCodeGrant-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $backupDir 'controlplus-before-grant.dump'
$sqlFile = Join-Path $env:TEMP ('ProductCodeGrant-' + [guid]::NewGuid().ToString('N') + '.sql')
$pgPassword = Read-Host 'Contraseña de postgres (oculta)' -AsSecureString
$credential = [pscredential]::new('postgres', $pgPassword)
$saved = @{}
foreach ($name in @('PGPASSWORD','PGOPTIONS','ConnectionStrings__ControlPlusDb',
    'CONTROLPLUS_MIGRATIONS_ENABLED','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf)) { throw 'Compilar y revisar la DLL nueva antes de aplicar.' }
    $env:PGPASSWORD = $credential.GetNetworkCredential().Password
    Remove-Item Env:PGOPTIONS -ErrorAction SilentlyContinue
    $pre = @'
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN current_database()='controlplus' AND current_user='postgres'
  AND (SELECT count(*) FROM public."__EFMigrationsHistory")=13
  AND (SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1)
      ='20261029010000_PermanentProductReferencesV1'
  AND to_regclass('catalogo.producto_codigo') IS NOT NULL
  AND EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app')
  AND NOT has_table_privilege('controlplus_app','catalogo.producto_codigo','SELECT')
  THEN 'PREFLIGHT_OK' ELSE 'PREFLIGHT_REJECTED' END;
ROLLBACK;
'@
    [IO.File]::WriteAllText($sqlFile, $pre, [Text.UTF8Encoding]::new($false))
    $preResult = @(& (Join-Path $pgBin 'psql.exe') -X -A -t -v ON_ERROR_STOP=1 -h 127.0.0.1 -p 5432 -U postgres -d controlplus -f $sqlFile)
    if ($LASTEXITCODE -ne 0 -or @($preResult | Where-Object { $_ -ceq 'PREFLIGHT_OK' }).Count -ne 1) {
        throw 'Preflight rechazado; no se creó respaldo ni se aplicó migración.'
    }
    New-Item -ItemType Directory -Path $backupDir -ErrorAction Stop | Out-Null
    & (Join-Path $pgBin 'pg_dump.exe') -h 127.0.0.1 -p 5432 -U postgres -d controlplus -Fc -f $backup
    if ($LASTEXITCODE -ne 0 -or (Get-Item -LiteralPath $backup).Length -le 0) { throw 'Respaldo fallido.' }
    & (Join-Path $pgBin 'pg_restore.exe') --file NUL $backup
    if ($LASTEXITCODE -ne 0) { throw 'El respaldo no pudo leerse por completo.' }
    Write-Host ('Respaldo verificado: ' + $backup)
    Write-Host ('SHA-256: ' + (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash)
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host']='127.0.0.1'; $connection['Port']=5432
    $connection['Database']='controlplus'; $connection['Username']='postgres'
    $connection['Password']=$credential.GetNetworkCredential().Password
    $env:ConnectionStrings__ControlPlusDb = $connection.psbase.ConnectionString
    $env:CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Set-Location (Join-Path $repo 'ControlPlus.Api')
    & dotnet $apiDll --migrate-to $target
    if ($LASTEXITCODE -ne 0) { throw 'Migrador fallido: verificar historial de solo lectura; no reintentar automáticamente.' }
    Remove-Item Env:ConnectionStrings__ControlPlusDb -ErrorAction SilentlyContinue
    $post = @'
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN current_database()='controlplus' AND current_user='postgres'
  AND (SELECT count(*) FROM public."__EFMigrationsHistory")=14
  AND (SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1)
      ='20261102010000_GrantProductCodeAccessV1'
  AND has_table_privilege('controlplus_app','catalogo.producto_codigo','SELECT')
  AND has_table_privilege('controlplus_app','catalogo.producto_codigo','INSERT')
  AND has_sequence_privilege('controlplus_app','catalogo.producto_referencia_seq','USAGE')
  AND has_function_privilege('controlplus_app','catalogo.siguiente_referencia_producto()','EXECUTE')
  AND has_function_privilege('controlplus_app','catalogo.reservar_codigo_producto(text,uuid)','EXECUTE')
  AND has_function_privilege('controlplus_app','catalogo.proteger_codigos_producto()','EXECUTE')
  AND has_function_privilege('controlplus_app','catalogo.registrar_codigos_producto()','EXECUTE')
  THEN 'POSTFLIGHT_OK' ELSE 'POSTFLIGHT_REJECTED' END;
ROLLBACK;
'@
    [IO.File]::WriteAllText($sqlFile, $post, [Text.UTF8Encoding]::new($false))
    $postResult = @(& (Join-Path $pgBin 'psql.exe') -X -A -t -v ON_ERROR_STOP=1 -h 127.0.0.1 -p 5432 -U postgres -d controlplus -f $sqlFile)
    if ($LASTEXITCODE -ne 0 -or @($postResult | Where-Object { $_ -ceq 'POSTFLIGHT_OK' }).Count -ne 1) {
        throw 'Postflight rechazado; revisar en solo lectura antes de iniciar API.'
    }
    Write-Host 'PRODUCT_CODE_GRANT_OK'
} finally {
    foreach ($name in $saved.Keys) {
        [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
    }
    if ($null -ne $connection) { $connection.Clear() }
    Remove-Item -LiteralPath $sqlFile -ErrorAction SilentlyContinue
    Set-Location $repo
    $credential = $null; $pgPassword = $null
}
```

Consulta para el preflight y postflight, en `psql -X -v ON_ERROR_STOP=1`
dentro de `BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;` y `ROLLBACK;`:

```sql
SELECT current_database(), current_user,
       (SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1) AS ultima;
SELECT to_regclass('catalogo.producto_codigo') IS NOT NULL AS tabla,
       EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app') AS rol;
SELECT has_table_privilege('controlplus_app','catalogo.producto_codigo','SELECT') AS tabla_select,
       has_table_privilege('controlplus_app','catalogo.producto_codigo','INSERT') AS tabla_insert,
       has_table_privilege('controlplus_app','catalogo.producto_codigo','UPDATE') AS tabla_update,
       has_table_privilege('controlplus_app','catalogo.producto_codigo','DELETE') AS tabla_delete,
       has_sequence_privilege('controlplus_app','catalogo.producto_referencia_seq','USAGE') AS secuencia_usage,
       has_sequence_privilege('controlplus_app','catalogo.producto_referencia_seq','SELECT') AS secuencia_select,
       has_sequence_privilege('controlplus_app','catalogo.producto_referencia_seq','UPDATE') AS secuencia_update;
SELECT p.proname, has_function_privilege('controlplus_app', p.oid, 'EXECUTE') AS ejecutable
FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
WHERE n.nspname='catalogo' AND p.proname IN
 ('siguiente_referencia_producto','reservar_codigo_producto',
  'proteger_codigos_producto','registrar_codigos_producto')
ORDER BY p.proname;
```

Postflight esperado: `ultima` igual al objetivo; SELECT/INSERT/USAGE verdaderos,
UPDATE/DELETE/SELECT secuencia/UPDATE secuencia falsos salvo concesiones ajenas,
y las cuatro funciones ejecutables. El preflight CLI existente de referencias
requiere que su objetivo esté pendiente, por lo que no corresponde a esta
migración. La gramática del ejecutable se conserva sin cambios.

## Comprobación HTTP ejecutada; no repetir sin causa nueva

La API debe iniciarse con `controlplus_app`, no con `postgres`, mediante el helper
de `docs/catalog-reference-startup.md`. El login normal puede crear su auditoría;
los GET no crean productos. En una segunda consola PowerShell 5.1:

```powershell
$ErrorActionPreference = 'Stop'
$baseUrl = 'http://localhost:5000'
$operator = Get-Credential -Message 'Usuario de ControlPlus con PRODUCTS.READ'
$login = $null; $token = $null; $body = $null; $headers = @{}
try {
    $body = ConvertTo-Json -InputObject @{
        userName = $operator.UserName
        password = $operator.GetNetworkCredential().Password
    } -Compress
    $login = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post `
        -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 20
    $body = $null
    $token = [string]$login.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'Login sin token.' }
    $headers['Authorization'] = 'Bearer ' + $token
    foreach ($case in @(
        @{ label = 'LISTADO'; search = $null },
        @{ label = 'QA'; search = 'QA-20261028-01' },
        @{ label = 'REFERENCIA'; search = 'P-000001' }
    )) {
        $uri = "$baseUrl/api/products?page=1&pageSize=20"
        if ($null -ne $case.search) { $uri += '&search=' + [Uri]::EscapeDataString($case.search) }
        $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method Get `
            -Headers $headers -TimeoutSec 20
        if ($response.StatusCode -ne 200) { throw ('HTTP inesperado en ' + $case.label) }
        $result = ConvertFrom-Json -InputObject $response.Content
        if ($case.label -eq 'REFERENCIA' -and $result.totalCount -ne 1) {
            throw 'La referencia P-000001 no resolvió un producto.'
        }
        Write-Host ($case.label + '_GET_OK status=200 total=' + $result.totalCount)
    }
    Write-Host 'PRODUCT_CODE_RUNTIME_GET_OK'
} finally {
    $headers.Clear(); $token = $null; $login = $null; $body = $null
    if ($null -ne $operator) { $operator.Password.Dispose() }
    $operator = $null
}
```

El operador informó `total=1` para `QA-20261028-01`; la búsqueda y el listado
responden HTTP 200. No se ejecutaron altas para esta verificación de lectura.
