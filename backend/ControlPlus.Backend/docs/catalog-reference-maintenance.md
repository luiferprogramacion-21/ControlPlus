# Catálogo: procedimiento Windows listo para revisión

**Activación retenida (2 de noviembre):** la [revisión de equivalencia del CHECK](testing/catalog-restore-equivalence-20261102.md)
identificó una diferencia de representación, corrigió el comparador y aprobó
39 comprobaciones focalizadas. No reejecutar el bloque anterior: cambió la huella
del helper. El siguiente paso es únicamente `Review-ExistingRestore.ps1 -ReadCurrent`,
conservando la API detenida, el clon y el respaldo.

El ensayo PGOPTIONS ya tiene evidencia revisada **6/6**. El
[bloque completo de activación](catalog-reference-activation.md) reutiliza este
procedimiento con respaldo nuevo, restauración comparada y una sola aplicación
persistente. Detener la API justo antes de ejecutarlo y mantener la ventana
hasta aceptar el postflight. No se ejecutó ese bloque desde Codex.

Actualización de esta etapa: el usuario confirmó el preflight real de lectura y
solicitó **respaldo nuevo y ensayo exclusivamente en un clon**. El bloque
autónomo está en [respaldo y ensayo del catálogo](catalog-reference-clone-rehearsal.md).
No aplica la migración al origen. Antes de ejecutarlo, mantener detenidos API,
frontend y todos los escritores; la actividad de API consignada en otras
evidencias no sustituye comprobar la ventana actual.

Preparado el **30 de octubre de 2026**. **Aplicación persistente de Catálogo pendiente**.
El operador confirmó el 31 de octubre `PREFLIGHT_LECTURA_OK` con doce predecesoras
(recibos ya aplicado) y referencias permanentes pendiente. La API sigue activa
con la DLL aislada de recibos. Véase la [corrección de PGOPTIONS y ensayo focalizado](testing/catalog-pgoptions-20261031.md):
restricción vigente contrastada con Fase 4, 450 comprobaciones offline y ensayo
PostgreSQL preparado, todavía sin resultado. Los bloques futuros no se ejecutaron.
Revisión offline ampliada el **31 de octubre**:
[validaciones, conciliación y bloque único de lectura](testing/catalog-reference-preflight-offline-20261031.md).
La etapa actual entrega solamente ese preflight; los bloques de respaldo y
aplicación de este documento siguen siendo propuestas para revisión posterior.
Tras un fallo genérico comunicado por el usuario, se preparó un
[ajuste de diagnóstico seguro y repetición de lectura](testing/catalog-preflight-diagnostic-20261031.md).
No hay código de salida ni causa técnica recuperables de la salida anterior.
Los bloques de conexión, respaldo, restauración y aplicación siguientes se
entregan para revisar y autorizar una ventana de mantenimiento posterior. Leerlos
no equivale a autorizar su ejecución. No se vuelve a ejecutar la suite aprobada.

## Evidencia y límite del migrador

Los TRX del 29 de octubre se leyeron el 30: CatalogMigration **6/6**, CatalogApi
**49/49**, Domain **70/70**; todos `Completed`, sin errores ni omisiones. El
[informe de referencias](permanent-product-references.md) conserva huellas y
detalle, además de Angular **32/32** y producción con sus tres avisos.

`ControlledMigrationRunner.RunAsync` valida un prefijo exacto del catálogo EF y
la secuencia pendiente. Su comportamiento **general** es aplicar toda la cadena
pendiente hasta el objetivo mediante `IMigrator.MigrateAsync(target)`; indicar
un objetivo no significa por sí solo aplicar una única migración.

Para `20261029010000_PermanentProductReferencesV1` existe una protección
adicional: `targetIndex == applied.Length`. Exige las **12 predecesoras** ya
aplicadas, terminando en `20261028010000_SaleReceiptDataSnapshotV1`; si falta
alguna, rechaza con salida 2 antes de migrar. El preflight específico exige el
mismo historial y ausencia de códigos ambiguos/objetos reservados, en
`REPEATABLE READ, READ ONLY` con rollback. No verifica por sí solo todos los
detalles del esquema; se complementa abajo con SQL y comparación de estructura.

Si el historial aún termina en `20261026010000_InitialProductInventoryMovementV1`,
**detenerse**: falta revisar y autorizar por separado la predecesora de recibos.
No se proporciona un salto, ni se aplica automáticamente esa predecesora. Su
objetivo genérico podría arrastrar otras pendientes. No se ha consultado aquí
el historial actual de la base real y no se afirma que recibos esté aplicada.

No existe `--postflight-to 20261029010000_PermanentProductReferencesV1` en el
parser actual: sería rechazado. El postflight de este procedimiento es SQL de
solo lectura más comparación de instantáneas, no un endpoint o modo inventado.

## Condiciones de la ventana

- Aprobación expresa de acceso, respaldo y aplicación persistente posterior a
  esta revisión; espacio disponible para respaldo y copia temporal.
- API y **todos** los escritores detenidos desde antes del primer preflight
  hasta concluir el postflight. Cerrar la API desde su consola/servicio habitual;
  no matar un PID sin identificarlo. Las comprobaciones de sesiones son una
  defensa adicional, no impiden futuras conexiones externas.
- PostgreSQL 18 nativo local, .NET 10 y PowerShell 5.1. Sin conexiones a nube,
  altas HTTP, restauración sobre la base real ni cambios de permisos automáticos.
- Rol propietario/migrador autorizado para la base real, solicitado explícitamente;
  **no** usar `controlplus_test_admin` allí. El rol exclusivo de pruebas se usa
  solo sobre `postgres` para administrar su clon `controlplus_test_<UUID>`.
- Los respaldos contienen datos sensibles. Directorio externo al repositorio,
  restringido al usuario Windows; no publicar archivos ni salida de datos.

## 0. Preparar la sesión y fijar artefactos (sin conexión)

Los helpers nuevos solo definen funciones al cargarse. No restauran paquetes,
compilan, ejecutan tests ni conectan por iniciativa propia. No contienen claves.
Usar los binarios Debug de la ejecución aprobada; si fueron reconstruidos o
cambiaron después, falta conciliar esa identidad antes de aplicar. Los TRX
no contienen una huella del ensamblado, por lo que no demuestran por sí solos
la identidad de los archivos actuales.

La conciliación posterior vinculó los PDB/PE con las 278 fuentes actuales y
contrastó las cuatro DLL con las copias actuales de pruebas; sus huellas y
límites están en el informe enlazado. El driver `Invoke-ReadOnlyPreflight.ps1`
comprueba el manifiesto antes de conectar. Fijar de nuevo las huellas con el
bloque antiguo siguiente no reemplaza esa conciliación ni autoriza a aceptar
un binario reconstruido diferente.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$sqlDir = Join-Path $repo 'scripts\catalog-references'
$apiDll = Join-Path $repo 'ControlPlus.Api\bin\Debug\net10.0\ControlPlus.Api.dll'
$apiDir = Split-Path -Parent $apiDll
$psTokens=$null; $psErrors=$null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $sqlDir 'Maintenance.Helpers.ps1'),[ref]$psTokens,[ref]$psErrors) | Out-Null
if ($psErrors.Count -ne 0) { throw 'Errores de sintaxis en los helpers; detener.' }
Write-Host 'MAINTENANCE_SYNTAX_OK'
. (Join-Path $sqlDir 'Maintenance.Helpers.ps1')
if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf)) { throw 'Falta el binario aprobado.' }

$backupParent = Join-Path $env:USERPROFILE 'ControlPlus-Backups'
$work = Join-Path $backupParent ('CatalogReferences-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = [System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true,$false)
$acl.SetOwner($sid)
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
    $sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $work -AclObject $acl

$pinned = @(Get-ChildItem -LiteralPath $apiDir -File | Where-Object {
    $_.Extension -in @('.dll','.json')
} | ForEach-Object {
    [pscustomobject]@{ Name=$_.Name; Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$pinned | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'artifacts.json') -Encoding UTF8
$reviewFiles = @('Maintenance.Helpers.ps1','Preflight.sql','Snapshot.sql','Postflight.sql')
$reviewHashes = @{}
foreach ($name in $reviewFiles) { $reviewHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $sqlDir $name) -Algorithm SHA256).Hash }
$reviewHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'review-files.json') -Encoding UTF8
function Assert-CatalogArtifacts {
    foreach ($name in $reviewFiles) {
        if ((Get-FileHash -LiteralPath (Join-Path $sqlDir $name) -Algorithm SHA256).Hash -cne $reviewHashes[$name]) {
            throw 'Cambio de SQL/helpers; detener y revisar.'
        }
    }
    foreach ($entry in $pinned) {
        $path = Join-Path $apiDir $entry.Name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.Sha256) {
            throw 'Cambio de artefactos; detener y conciliar version.'
        }
    }
}
$clone = 'controlplus_test_' + [guid]::NewGuid().ToString('N')
$dump = Join-Path $work 'controlplus-before-catalog.dump'
```

Mantener esta misma sesión para los bloques siguientes. Una vez autorizada la
ventana, solicitar claves **ocultas**, sin compartirlas ni usar User Secrets de
la API para deducir el rol:

```powershell
$realUser = Read-Host 'Rol propietario/migrador autorizado para controlplus'
if ([string]::IsNullOrWhiteSpace($realUser) -or $realUser -eq 'controlplus_test_admin') {
    throw 'Se requiere un rol persistente autorizado distinto del rol de pruebas.'
}
$realSecret = Read-Host 'Contrasena de ese rol' -AsSecureString
$realCredential = [pscredential]::new($realUser,$realSecret)
$testSecret = Read-Host 'Contrasena de controlplus_test_admin' -AsSecureString
$testCredential = [pscredential]::new('controlplus_test_admin',$testSecret)
```

## 1. Preflight de historial y esquema (conexión real de solo lectura)

Para la revisión actual usar **únicamente el bloque autónomo** del
[informe offline](testing/catalog-reference-preflight-offline-20261031.md#bloque-único-de-lectura-para-windows-powershell-51).
Incluye destino explícito sin secretos, historial completo y 12 predecesoras,
esquema, estado del objetivo e impedimentos. No ejecuta los pasos 0, 2 o 3 ni
prepara respaldo. Cero clientes visibles con visibilidad parcial bloquea la
continuación; no se cambian permisos automáticamente. El rol de pruebas no
tiene por defecto visibilidad global, por lo que el futuro ensayo debe resolver
ese requisito documentadamente, sin eludirlo ni usar el usuario de la aplicación.

El bloque siguiente pertenece al **procedimiento futuro de mantenimiento**.
Ejecutar únicamente tras aprobar el acceso. Ambos pasos deben terminar con
salida 0; si faltan predecesoras, parar y preparar su revisión separada.

```powershell
Assert-CatalogArtifacts
Invoke-CatalogSqlFile -PgBin $pgBin -Credential $realCredential -Database controlplus `
    -SqlFile (Join-Path $sqlDir 'Preflight.sql')
Invoke-CatalogControlledMode -Mode Preflight -Database controlplus `
    -Credential $realCredential -ApiDll $apiDll
Save-CatalogSnapshot -PgBin $pgBin -Credential $realCredential -Database controlplus `
    -SqlDirectory $sqlDir -Path (Join-Path $work 'before.jsonl')
```

Se muestra historial y definiciones de restricciones, no filas de negocio.
Revisar `ck_producto_formato_codigo` contra la definición inicial oficial y las
columnas esperadas de la migración. El SQL complementario rechaza triggers o
reglas de producto preexistentes que necesiten revisión por sus posibles efectos
durante el backfill. No elimina esos objetos para continuar.

## 2. Respaldo, restauración temporal y ensayo del cambio

Para esta etapa usar el **bloque autónomo actualizado** de
[respaldo y ensayo](catalog-reference-clone-rehearsal.md#bloque-operativo-windows-powershell-51).
Fija los artefactos con el manifiesto conciliado, conserva las evidencias y añade
contraste exacto del CHECK de barras mediante DDL temporal y rollback solo en
el clon. El rol de pruebas escribe en el clon; el lector autorizado realiza
las comprobaciones de visibilidad global, sin conceder permisos nuevos.

La primera llamada solo lee la base real. Hash y TOC legible **no bastan**: el
criterio de éxito incluye restauración completa y comparación contra la base
anterior. Primero verificar el rol de pruebas sobre `postgres`:

```powershell
$probe = @(Invoke-CatalogPgTool -PgBin $pgBin -Tool psql -Credential $testCredential -ReadOnly -PgArguments @(
    '-X','-q','-A','-t','-d','postgres','-v','ON_ERROR_STOP=1','-c',
    "SELECT current_database()='postgres' AND current_user='controlplus_test_admin' AND rolcanlogin AND rolcreatedb AND NOT rolsuper AND current_setting('server_version_num')::int BETWEEN 180000 AND 189999 FROM pg_roles WHERE rolname=current_user;"))
if ($probe.Count -ne 1 -or $probe[0] -cne 't') { throw 'Rol/servidor de pruebas incompatible.' }

Invoke-CatalogPgTool -PgBin $pgBin -Tool pg_dump -Credential $realCredential -ReadOnly `
    -PgArguments @('--format=custom','--dbname=controlplus','--file',$dump) | Out-Null
if ((Get-Item -LiteralPath $dump).Length -le 0) { throw 'Respaldo vacio.' }
$dumpHash = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
$dumpHash | Set-Content -LiteralPath (Join-Path $work 'dump.sha256') -Encoding ASCII
Invoke-CatalogPgTool -PgBin $pgBin -Tool pg_restore -Credential $testCredential `
    -PgArguments @('--list',$dump) | Set-Content -LiteralPath (Join-Path $work 'dump.toc') -Encoding UTF8

if ($clone -cnotmatch '^controlplus_test_[a-f0-9]{32}$') { throw 'Nombre de clon rechazado.' }
$sourceDb = (Read-CatalogSnapshot (Join-Path $work 'before.jsonl'))['database:attributes']
$providers = @{ c='libc'; i='icu'; b='builtin' }
if (-not $providers.ContainsKey([string]$sourceDb.provider)) { throw 'Proveedor de locale desconocido.' }
$createArgs = @('--maintenance-db=postgres','--template=template0','--owner=controlplus_test_admin',
    '--encoding',([string]$sourceDb.encoding),'--locale-provider',([string]$providers[[string]$sourceDb.provider]),
    '--lc-collate',([string]$sourceDb.collate),'--lc-ctype',([string]$sourceDb.ctype))
if ($sourceDb.provider -eq 'i') {
    $createArgs += @('--icu-locale',([string]$sourceDb.locale))
    if (-not [string]::IsNullOrEmpty([string]$sourceDb.icuRules)) { $createArgs += @('--icu-rules',([string]$sourceDb.icuRules)) }
} elseif ($sourceDb.provider -eq 'b') { $createArgs += @('--builtin-locale',([string]$sourceDb.locale)) }
$createArgs += $clone
Invoke-CatalogPgTool -PgBin $pgBin -Tool createdb -Credential $testCredential `
    -PgArguments $createArgs | Out-Null
Invoke-CatalogPgTool -PgBin $pgBin -Tool pg_restore -Credential $testCredential `
    -PgArguments @('--exit-on-error','--single-transaction','--no-owner','--no-privileges','--dbname',$clone,$dump) | Out-Null
Save-CatalogSnapshot -PgBin $pgBin -Credential $testCredential -Database $clone `
    -SqlDirectory $sqlDir -Path (Join-Path $work 'restored.jsonl')
Assert-CatalogSnapshot -Before (Join-Path $work 'before.jsonl') `
    -After (Join-Path $work 'restored.jsonl') -Mode Restore

Assert-CatalogArtifacts
Invoke-CatalogSqlFile -PgBin $pgBin -Credential $realCredential -Database $clone `
    -SqlFile (Join-Path $sqlDir 'Preflight.sql')
Invoke-CatalogControlledMode -Mode Preflight -Database $clone -Credential $testCredential -ApiDll $apiDll
Invoke-CatalogControlledMode -Mode Apply -Database $clone -Credential $testCredential -ApiDll $apiDll
Invoke-CatalogSqlFile -PgBin $pgBin -Credential $realCredential -Database $clone `
    -SqlFile (Join-Path $sqlDir 'Postflight.sql')
Save-CatalogSnapshot -PgBin $pgBin -Credential $testCredential -Database $clone `
    -SqlDirectory $sqlDir -Path (Join-Path $work 'clone-migrated.jsonl')
Assert-CatalogSnapshot -Before (Join-Path $work 'restored.jsonl') `
    -After (Join-Path $work 'clone-migrated.jsonl') -Mode Upgrade
```

`--no-owner --no-privileges` permite que el rol exclusivo posea los objetos del
clon. Se replica codificación y locale del origen; si su versión de collation
no coincide, la comparación falla y requiere revisión separada. Se verifica
estructura y contenido; **no** acredita restaurar propietarios,
ACL ni roles globales de producción. El dump conserva la información original;
una restauración persistente de emergencia exige su propio procedimiento y
autorización. No cambiar de rol a superusuario si el ensayo falla. Conservar la
copia y la evidencia hasta resolverlo. No iniciar la API contra ese clon ni
hacer altas de prueba. Ensayar el respaldo actual tiene una causa nueva y no
repite las suites aprobadas.

## 3. Aplicación persistente exclusiva del objetivo (pendiente de autorización)

Requiere respaldo y ensayo anteriores acreditados, mismo binario y ventana sin
escritores. La comparación adicional exige que la base real no haya cambiado
desde el respaldo. No genera otro respaldo ni salta discrepancias para continuar.

```powershell
Assert-CatalogArtifacts
if ((Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash -cne $dumpHash) {
    throw 'El respaldo cambio; detener.'
}
Invoke-CatalogSqlFile -PgBin $pgBin -Credential $realCredential -Database controlplus `
    -SqlFile (Join-Path $sqlDir 'Preflight.sql')
Save-CatalogSnapshot -PgBin $pgBin -Credential $realCredential -Database controlplus `
    -SqlDirectory $sqlDir -Path (Join-Path $work 'before-apply.jsonl')
Assert-CatalogSnapshot -Before (Join-Path $work 'before.jsonl') `
    -After (Join-Path $work 'before-apply.jsonl') -Mode Restore
Invoke-CatalogControlledMode -Mode Preflight -Database controlplus `
    -Credential $realCredential -ApiDll $apiDll
Invoke-CatalogControlledMode -Mode Apply -Database controlplus `
    -Credential $realCredential -ApiDll $apiDll
```

El helper ejecuta, desde el directorio del DLL y un proceso separado de la API:

```powershell
# Forma interna exacta, con conexion en memoria y entorno exclusivo del hijo:
# CONTROLPLUS_MIGRATIONS_ENABLED = 'true'
# dotnet <DLL fijado> --preflight-to 20261029010000_PermanentProductReferencesV1
# dotnet <DLL fijado> --migrate-to 20261029010000_PermanentProductReferencesV1
```

La conexión fija `Host=127.0.0.1`, `Port=5432`, base y usuario de la fase;
`ConnectionStrings__ControlPlusDb` prevalece sobre configuración local. No hay
clave en argumentos ni salida. Gate, conexión y entorno solo se configuran en
el hijo; las copias se limpian al salir y el entorno del padre no se modifica.
No arranca HTTP. No usar `dotnet ef database update`, migración sin objetivo ni
objetivos anteriores para eludir el rechazo de predecesoras.

Un fallo al COMMIT o en la comprobación posterior puede dejar resultado incierto:
mantener la API detenida, comprobar historial/esquema en solo lectura y decidir
tras revisar evidencia. **No** reintentar automáticamente, invocar `Down` ni
restaurar sobre la base real como respuesta automática al error.

## 4. Postflight persistente (solo lectura)

```powershell
Assert-CatalogArtifacts
Invoke-CatalogSqlFile -PgBin $pgBin -Credential $realCredential -Database controlplus `
    -SqlFile (Join-Path $sqlDir 'Postflight.sql')
Save-CatalogSnapshot -PgBin $pgBin -Credential $realCredential -Database controlplus `
    -SqlDirectory $sqlDir -Path (Join-Path $work 'after.jsonl')
Assert-CatalogSnapshot -Before (Join-Path $work 'before-apply.jsonl') `
    -After (Join-Path $work 'after.jsonl') -Mode Upgrade
Assert-CatalogSnapshot -Before (Join-Path $work 'clone-migrated.jsonl') `
    -After (Join-Path $work 'after.jsonl') -Mode MigratedCopy
```

Debe acreditar `POSTFLIGHT_SQL_OK`, `SNAPSHOT_UPGRADE_OK` y
`SNAPSHOT_MIGRATEDCOPY_OK`. El historial debe ser exactamente el anterior más
una fila del objetivo (13 filas con el catálogo actual). UUID y cantidad de
productos iguales; referencia `P-` de seis o más dígitos, única y registrada;
versión anterior +1 por producto. Todas las demás columnas de producto, incluidas
barras, formato, generación, costos, stock, estado y relaciones, conservan su
huella. Cada código anterior y barra conserva alias al mismo UUID, con el conjunto
exacto de alias anteriores más referencias nuevas. Se leen estado de secuencia,
índices, restricciones y triggers sin llamar `nextval` ni escribir.

Todas las demás tablas (ventas, detalles, movimientos, auditoría, idempotencia,
documentos, seguridad, etc.) mantienen conteo y huella SHA-256 del multiconjunto
de filas; sus estructuras y secuencias anteriores también se comparan. La fila
nueva de historial y `producto_codigo` son las únicas tablas adicionales o con
datos diferentes permitidos, además de los cambios expresos de producto.
Se conservan las definiciones de funciones/procedimientos anteriores y se
permiten únicamente las cuatro funciones nuevas del registro de códigos.
Entre clon migrado y base real se permite solo la
fecha de registro diferente de los alias. Las definiciones nuevas se muestran
para revisión y la estructura se compara con el ensayo del mismo binario.

Las instantáneas contienen metadatos, UUID y huellas, no códigos, cuerpos HTTP,
contraseñas, tokens ni cadenas de conexión. Las huellas no sustituyen el respaldo
ni la ventana sin escritores. El dump sí contiene datos y requiere custodia.

## 5. Cierre de sesión y copia temporal

Tras cualquier error, conservar la evidencia y cerrar credenciales en memoria:

```powershell
if ($null -ne $realSecret) { $realSecret.Dispose() }
if ($null -ne $testSecret) { $testSecret.Dispose() }
$realCredential=$null; $testCredential=$null; $realSecret=$null; $testSecret=$null
```

Solo tras aceptar el postflight, reanudar los escritores habituales; la comparación
visual y cualquier recuperación del alta pendiente se revisan por separado. Este
procedimiento no cambia claves de idempotencia ni vuelve a enviar el alta.

Para retirar el clon propio, en una sesión autorizada y con la contraseña del rol
de pruebas nuevamente oculta (no se elimina el respaldo):

```powershell
if ($clone -cnotmatch '^controlplus_test_[a-f0-9]{32}$') { throw 'Clon rechazado.' }
$cleanupSecret = Read-Host 'Contrasena de controlplus_test_admin' -AsSecureString
try {
    $cleanupCredential = [pscredential]::new('controlplus_test_admin',$cleanupSecret)
    Invoke-CatalogPgTool -PgBin $pgBin -Tool dropdb -Credential $cleanupCredential `
        -PgArguments @('--maintenance-db=postgres',$clone) | Out-Null
} finally { $cleanupSecret.Dispose(); $cleanupCredential=$null; $cleanupSecret=$null }
```

## Pendientes y límites de esta preparación

El agente no ejecutó estos bloques ni abrió la base real en esta preparación.
El usuario sí comunicó `PREFLIGHT_LECTURA_OK` sobre el origen; respaldo y ensayo
del clon siguen pendientes de su ejecución.
Los helpers iniciales tienen 27 comprobaciones nativas PS5.1 comunicadas por el
usuario. Las incorporaciones del preflight y su protección nueva de libpq
pasaron 374 comprobaciones portables; el usuario confirmó además las 374 nativas
con `PREFLIGHT_OFFLINE_WINDOWS_PS51_OK checks=374` en Windows PowerShell 5.1.
La conciliación de artefactos está documentada con sus límites: los TRX carecen
de hashes históricos. Pendientes: respaldo actual,
restauración comparada, ensayo de la copia,
aplicación persistente autorizada y postflight. La comparación visual renderizada
sigue pendiente. No se modificó la migración, el backend ni las dependencias.

Referencias de herramientas: [pg_dump 18](https://www.postgresql.org/docs/18/app-pgdump.html),
[pg_restore 18](https://www.postgresql.org/docs/18/app-pgrestore.html),
[createdb 18](https://www.postgresql.org/docs/18/app-createdb.html),
[atributos de base](https://www.postgresql.org/docs/18/catalog-pg-database.html),
[SHA-256 nativo](https://www.postgresql.org/docs/18/functions-binarystring.html).
