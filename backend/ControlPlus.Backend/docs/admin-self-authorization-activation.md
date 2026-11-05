# Ensayo y activación controlada de tres migraciones de autorización

**Estado: tres migraciones aplicadas y cierre final de solo lectura acreditado según el recibo comunicado por el operador.** Este documento es para el operador en Windows PowerShell 5.1, desde `C:\ControlPlus\backend\ControlPlus.Backend`. El agente no ejecutó los modos `Rehearsal` ni `Apply`, ni consultó `controlplus`. La prueba automatizada anterior de las tres migraciones sobre una base temporal terminó 1/1; véase `docs/testing.md`.

## Orden y alcance

1. `20261104010000_AdminSelfSaleCancellationV1`: sustituye `ck_anulacion_venta_usuarios` por el trigger limitado a la autoautorización de Administrador.
2. `20261104020000_AdminSelfCashDifferenceV1`: reemplaza las definiciones de los dos validadores de Caja.
3. `20261105010000_SupervisorLayawayRefundDecisionV1`: añade exactamente la concesión `LAYAWAY.REFUND_DECIDE` a la plantilla del Supervisor.

Se espera el historial **exacto de 15 predecesoras** con `20261102020000_CategoryProductReferencesV1` como última. Un historial diferente detiene el procedimiento; no se omiten ni se aplican migraciones intermedias. Los preflight SQL de cada paso verifican el objeto antiguo y que el objetivo aún no esté aplicado. El tercer preflight exige que la concesión del Supervisor todavía no exista. Los postflight confirman los objetos nuevos y el historial. `SourceGuard.sql` verifica PostgreSQL 18, destino/rol, historial esperado y ausencia de otras sesiones cliente en la base de origen.

La instantánea de `scripts/catalog-references/Snapshot.sql` registra hashes de filas de **todas** las tablas, historial, secuencias, rutinas y forma de cada tabla (columnas, CHECK, índices y triggers). El comparador solo permite en cada etapa los cambios listados arriba y una fila nueva de historial. En la tercera etapa exige exactamente una fila adicional en `seguridad.rol_permiso`. El ensayo compara origen, respaldo restaurado y origen final. La aplicación compara su estado con el origen y los resultados del ensayo. Ante cualquier diferencia se detiene sin reintento automático.

## Preparación y ensayo en clon nuevo

Detener API, frontend y cualquier escritor; confirmar la ventana de mantenimiento. **PostgreSQL 18 debe estar activo y aceptar conexiones en TCP 5432**. El guard acepta el servicio de Windows en `Running` o un servidor arrancado por otra vía, pero solo si el PID de `postmaster.pid`, el directorio de datos del servicio, `PG_VERSION=18` y la hora de creación del proceso coinciden. `pg_isready -h 127.0.0.1 -p 5432` confirma disponibilidad sin ejecutar SQL; si Windows muestra un dueño distinto del puerto, el guard rechaza. `Get-NetTCPConnection` y `netstat` pueden no mostrar el listener aun cuando `pg_isready` devuelve 0, como ocurrió en el diagnóstico del 5 de noviembre. No borrar `postmaster.pid` ni intentar iniciar otro servidor sobre el mismo directorio. `-Mode ServerProbeOnly` hace estas comprobaciones sin consulta SQL. Compilar **en serie** la API actual, con cero errores y advertencias, antes del ensayo. La copia del binario ensayado queda congelada en la evidencia. El ensayo lee el origen y escribe únicamente en un clon nuevo; el origen debe permanecer sin cambios. El rol `controlplus_test_admin` debe disponer de permiso para crear/restaurar el clon. Las contraseñas se piden ocultas y se entregan solo como variables de proceso a cada herramienta; ningún archivo de evidencia incluye cadenas de conexión o secretos.

```powershell
& {
$ErrorActionPreference = 'Stop'
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
.\scripts\admin-self-authorization\Invoke-AdminSelfActivation.ps1 -Mode ServerProbeOnly
dotnet restore .\ControlPlus.Api\ControlPlus.Api.csproj --force -m:1 /p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw 'Falló restore; no iniciar ensayo.' }
dotnet build .\ControlPlus.Api\ControlPlus.Api.csproj --no-restore -m:1 /p:BuildInParallel=false /warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Falló build; no iniciar ensayo.' }
$rehearsal = & .\scripts\admin-self-authorization\Invoke-AdminSelfActivation.ps1 -Mode Rehearsal -MaintenanceConfirmed
if ([string]::IsNullOrWhiteSpace($rehearsal)) { throw 'Ensayo detenido; no hay recibo ni aplicación.' }
if (-not (Test-Path -LiteralPath (Join-Path $rehearsal 'result.json'))) { throw 'Ensayo sin recibo; no aplicar.' }
$result = Get-Content -LiteralPath (Join-Path $rehearsal 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($result.status -cne 'ADMIN_SELF_REHEARSAL_OK' -or $result.cloneHistory -ne 18 -or -not $result.sourcePreserved) {
    throw 'Ensayo no acreditado; no aplicar.'
}
Write-Host "Ensayo acreditado: $rehearsal"
Write-Host "Clon: $($result.clone)"
Write-Host "Respaldo SHA-256: $($result.backupSha256)"
Write-Host ('Recibo SHA-256: ' + (Get-FileHash -LiteralPath (Join-Path $rehearsal 'result.json') -Algorithm SHA256).Hash)
}
```

Conservar el clon, `result.json`, las instantáneas y el respaldo para revisión. **No pasar directamente al bloque real si el ensayo falla o si el origen cambia después del ensayo.** Revisar también los siete controles SQL y los tres registros `*-apply.txt` en la carpeta de evidencia. El recibo fija hashes de **todos los archivos de `api-bin`**, fuentes de las migraciones, script, helper, SQL y snapshots; el bloque real los vuelve a comprobar. No ejecutar `Down`.

## Bloque histórico de aplicación real — ejecutado; no repetir

Este bloque se ejecutó una vez por el operador. La ruta y el SHA-256 del recibo aprobado estaban fijados. El script solicitó la contraseña de `postgres` con `Read-Host -AsSecureString` y ejecutó `--migrate-to` una vez por objetivo desde un proceso separado de la API. **No repetirlo**: las tres migraciones y los tres postflight ya aprobaron en `controlplus`. El marcador final se detuvo en la comparación descrita abajo.

```powershell
& {
$ErrorActionPreference = 'Stop'
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
$rehearsal = 'C:\Users\luife\ControlPlus-Backups\AdminSelf-Rehearsal-20261105T204319Z-a632e5f5996e40c296334d7fd5bc971f'
$receiptSha256 = '438F946AEEDB6096B317FE4B92229CA6500937AE5455BB8D9B47C98CFDC09D08'
if (-not (Test-Path -LiteralPath (Join-Path $rehearsal 'result.json'))) { throw 'Falta el ensayo aprobado.' }
if ((Get-FileHash -LiteralPath (Join-Path $rehearsal 'result.json') -Algorithm SHA256).Hash -cne $receiptSha256) {
    throw 'El recibo cambió desde su revisión.'
}
$receipt = Get-Content -LiteralPath (Join-Path $rehearsal 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($receipt.status -cne 'ADMIN_SELF_REHEARSAL_OK' -or $receipt.sourceHistory -ne 15 -or
    $receipt.cloneHistory -ne 18 -or -not $receipt.sourcePreserved -or
    @($receipt.artifactFiles).Count -ne 160) { throw 'Ensayo incompatible.' }
Write-Host ('Ensayo: ' + $rehearsal)
Write-Host ('Binario SHA-256: ' + $receipt.apiSha256)
Write-Host ('Respaldo de ensayo SHA-256: ' + $receipt.backupSha256)
$activation = & .\scripts\admin-self-authorization\Invoke-AdminSelfActivation.ps1 `
    -Mode Apply -MaintenanceConfirmed -ApprovedRehearsal $rehearsal -ExpectedRehearsalSha256 $receiptSha256
if ([string]::IsNullOrWhiteSpace($activation)) { throw 'Activación detenida; no hay recibo final.' }
if (-not (Test-Path -LiteralPath (Join-Path $activation 'result.json'))) { throw 'Activación sin recibo.' }
$final = Get-Content -LiteralPath (Join-Path $activation 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($final.status -cne 'ADMIN_SELF_ACTIVATION_OK') { throw 'Activación sin marcador final.' }
Write-Host ('ADMIN_SELF_ACTIVATION_OK; evidencia: ' + $activation)
Write-Host ('Respaldo real SHA-256: ' + $final.backupSha256)
}
```

El modo real crea una carpeta privada **nueva** con marca UTC en `%USERPROFILE%\ControlPlus-Backups`, un respaldo custom nuevo, SHA-256 y TOC. Lee el respaldo completo con `pg_restore --file NUL`; el ensayo ya verificó además una restauración verdadera en el clon. Toma instantáneas antes y después del respaldo y de cada migración; confirma que el binario, el script, los SQL y el respaldo no cambien durante la ejecución. El proceso no arranca HTTP ni usa las credenciales de la aplicación.

## Fallo y reversa

Si falla una etapa, conservar la API detenida y la carpeta con `failure.json`, el respaldo y las instantáneas. Consultar **solo en lectura** el historial y los objetos para determinar el punto exacto; no repetir el bloque automáticamente, ya que una o dos migraciones pueden haber quedado aplicadas. No ejecutar `Down` en `controlplus`: las tres migraciones lo impiden expresamente. Para ensayar la reversa, restaurar el respaldo **en otro clon nuevo** `controlplus_test_*` y cotejarlo con la instantánea `source-before.jsonl`; el plan de recuperación de la base real requiere una decisión operativa aparte. No sobrescribir la base real con este procedimiento.

## Validación offline

Estos comandos no abren conexión. El primero comprueba sintaxis y presencia/formato de los siete SQL de lectura y tres migraciones; el segundo ejecuta el comparador sobre instantáneas sintéticas, incluidos cambios ajenos y una segunda concesión del permiso que deben ser rechazados. El tercero comprueba la equivalencia archivada del clon restaurado y rechaza tres mutaciones.

```powershell
Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
.\scripts\admin-self-authorization\Invoke-AdminSelfActivation.ps1 -Mode OfflineValidateOnly
.\scripts\admin-self-authorization\Test-AdminSelfActivationOffline.ps1
.\scripts\admin-self-authorization\Test-AdminSelfRestoreOffline.ps1 `
    -EvidenceDirectory 'C:\Users\luife\ControlPlus-Backups\AdminSelf-Rehearsal-20261105T203114Z-0b87695bbca746b6a96977ac80b59f04'
```

La validación offline no acredita la disponibilidad de PostgreSQL, la parada real de la API, la restauración ni la aplicación. Esas comprobaciones pertenecen al ensayo y a la ejecución del operador.

## Primer ensayo detenido y comparador corregido (5 de noviembre de 2026)

La carpeta `C:\Users\luife\ControlPlus-Backups\AdminSelf-Rehearsal-20261105T203114Z-0b87695bbca746b6a96977ac80b59f04` contiene `failure.json` con `stage=RestoreClone` y `applyInvoked=false`. El preflight del origen y el respaldo terminaron; las capturas del origen antes y después del respaldo son iguales. El clon se restauró, pero `Assert-CatalogSnapshot -Mode Restore` rechazó sus metadatos antes de invocar el migrador. No hay registro `*-apply.txt` ni `result.json` de éxito de este intento. No se aplicó ninguna de las tres migraciones al clon ni a `controlplus`.

La comparación offline de sus 234 claves por instantánea encontró solo `shape:catalogo.producto.relationsHash` y `shape:compras.movimiento_inventario.relationsHash` distintos. Las huellas actuales de origen y clon coinciden **por separado** con el par de instantáneas históricas del ensayo de referencias de categoría, cuyos tamaños y SHA-256 están fijados por `docs/testing/category-reference-approved-clone-20261102.json` (SHA-256 del recibo `D22AD75E6E90E3445F4745EF03E20B5021D5960EF6E85CCED931DA2A54A3626B`). La diferencia histórica se acreditó como los casts equivalentes de `ck_producto_formato_codigo` y `ck_movimiento_inventario_tipo`; véase `docs/testing/category-reference-clone-restore-difference-20261102.md`. El comparador nuevo exige **ese par exacto de hashes** para Producto, usa la equivalencia acotada existente para Inventario y mantiene igualdad estricta para todas las demás claves. También se usa al comparar el resultado real con el clon ensayado.

`Test-AdminSelfRestoreOffline.ps1 -EvidenceDirectory <carpeta>` pasó 5/5: aceptó el par archivado y una copia serializada, y rechazó alteraciones sintéticas de una tabla ajena y de ambos CHECK. `OfflineValidateOnly` volvió a pasar. Las pruebas no abrieron conexiones ni aplicaron migraciones. El intento detenido se conserva; un ensayo nuevo crea otro respaldo y otro clon, sin reutilizar ni modificar ese intento.

## Segundo ensayo completo y cierre del manifiesto binario

El segundo ensayo, `C:\Users\luife\ControlPlus-Backups\AdminSelf-Rehearsal-20261105T203753Z-80d88a93e8ef41f891fece171a0edef7`, terminó `ADMIN_SELF_REHEARSAL_OK`: historial del clon 15→18, tres `*-apply.txt` de éxito, los tres postflight conformes y `source-after.jsonl` idéntico a `source-before.jsonl`. El recibo archivado tiene SHA-256 `F222D8CE6A967D910B1A8287909BEDF6298C076910A2B8FC1D2FE5A5608F30B2`; su respaldo tiene SHA-256 `FF5ACBEAD5ED33D4937572784014532CCECBB2A8F5A5455506058458A4430D92`. No hubo aplicación a `controlplus`.

La revisión detectó una guarda incompleta **para la aplicación real**: ese recibo fijaba `ControlPlus.Api.dll`, pero no las demás DLL y archivos cargados desde `api-bin`. El activador ahora incluye un manifiesto de los **160 archivos** del directorio en `result.json` y comprueba nombres, tamaños y SHA-256 antes de cada migración real. La prueba offline `Test-AdminSelfBinaryManifestOffline.ps1` aprobó 4/4 sobre el binario ensayado: conjunto íntegro, lectura desde JSON y rechazo de un hash cambiado o un archivo omitido. Esta mejora cambió la huella del activador, de modo que **el segundo recibo no se debe usar para el bloque real**. Se exigió un tercer ensayo nuevo, acreditado a continuación. Los dos ensayos anteriores se conservan; no se repite ninguna migración sobre ellos ni sobre `controlplus`.

## Tercer ensayo acreditado para la aplicación real

El tercer ensayo está en `C:\Users\luife\ControlPlus-Backups\AdminSelf-Rehearsal-20261105T204319Z-a632e5f5996e40c296334d7fd5bc971f`. Su `result.json` tiene SHA-256 `438F946AEEDB6096B317FE4B92229CA6500937AE5455BB8D9B47C98CFDC09D08` y declara `ADMIN_SELF_REHEARSAL_OK`, origen 15, clon 18 y `sourcePreserved=true`. La revisión independiente de archivos confirmó SHA-256 del respaldo, seis instantáneas fijadas y el archivo `dump.sha256`, 13 fuentes/SQL actuales y los **160 archivos binarios** sin omisiones ni duplicados. Los tres `*-apply.txt` registran éxito para los tres objetivos exactos y los tres postflight tienen sus marcadores `*_OK`. `source-before.jsonl` y `source-after.jsonl` son byte a byte iguales. Este es el único recibo usado por el bloque real histórico de arriba; las tres migraciones se aplicaron después como se documenta a continuación.

## Aplicación real: tres migraciones aplicadas y cierre de lectura acreditado

La carpeta `C:\Users\luife\ControlPlus-Backups\AdminSelf-Apply-20261105T205249Z-add103bff6954dcca1c9fcf1ce7bf1bb` conserva el respaldo real, las capturas y `failure.json` (`stage=Postflight`, `applyInvoked=true`). Los tres archivos `controlplus-<MigrationId>-apply.txt` registran éxito de las tres migraciones en orden; los postflight `SALE_SELF_POSTFLIGHT_OK`, `CASH_SELF_POSTFLIGHT_OK` y `SUPERVISOR_REFUND_POSTFLIGHT_OK` también aprobaron. `stage-2-after.jsonl` muestra historial 18 y `seguridad.rol_permiso` 104→105 filas. **No volver a ejecutar `--migrate-to` ni `Down`.**

El marcador final inicial se detuvo al comparar el hash completo de `seguridad.rol_permiso` entre clon y real. La tercera migración inserta solo `(rol_id, permiso_id)`; el DDL del respaldo fija `fecha_asignacion DEFAULT CURRENT_TIMESTAMP`, generado en cada transacción. Los resultados archivados de las etapas 0 y 1 solo difieren en los dos CHECK de casts ya acreditados; en la etapa 2 aparece además **únicamente** el hash de esta tabla. El comparador usado en el bloque real omitió reconocer ese valor variable. La conciliación posterior de solo lectura terminó con `ADMIN_SELF_ACTIVATION_OK`, según la salida comunicada por el operador.

`Complete-AdminSelfActivation.ps1` cierra **sin migrador, sin respaldo ni cambios de esquema**. Primero fija el recibo, las confirmaciones, postflight, backup y 160 binarios del intento detenido. Luego toma una instantánea de solo lectura de `controlplus` y exige que coincida con `stage-2-after.jsonl`. Repite los tres postflight y compara con el clon: cantidad 105, una sola concesión objetivo, default de fecha, hash íntegro de las 104 filas anteriores y hash lógico de todas las filas excluyendo solo `fecha_asignacion`. Tras esa conciliación, aplica el comparador estricto al resto de datos y a los casts históricamente aprobados. Solo entonces escribe `ADMIN_SELF_ACTIVATION_OK` en una carpeta nueva `ControlPlus-Reviews\AdminSelf-Completion-*`.

La validación `-OfflineValidateOnly` del cierre pasó en Windows PowerShell 5.1 sobre los archivos archivados: tres confirmaciones y tres postflight, recibo, respaldo, fuentes, 160 binarios y comparación de todas las otras claves; **no abrió la base**. La consulta SQL de conciliación está en `RolePermissionReconcile.ReadOnly.sql` y se ejecutó únicamente por el operador, en transacciones `READ ONLY`. El operador comunicó `SNAPSHOT_RESTORE_OK`, `ADMIN_SELF_RESTORED_CLONE_OK` y `ADMIN_SELF_ACTIVATION_OK`.

### Bloque histórico de cierre de solo lectura — ejecutado; no repetir

```powershell
& {
    $ErrorActionPreference = 'Stop'
    Set-Location 'C:\ControlPlus\backend\ControlPlus.Backend'
    $completion = & .\scripts\admin-self-authorization\Complete-AdminSelfActivation.ps1 `
        -FailedActivation 'C:\Users\luife\ControlPlus-Backups\AdminSelf-Apply-20261105T205249Z-add103bff6954dcca1c9fcf1ce7bf1bb' `
        -ApprovedRehearsal 'C:\Users\luife\ControlPlus-Backups\AdminSelf-Rehearsal-20261105T204319Z-a632e5f5996e40c296334d7fd5bc971f' `
        -ExpectedRehearsalSha256 '438F946AEEDB6096B317FE4B92229CA6500937AE5455BB8D9B47C98CFDC09D08' `
        -MaintenanceConfirmed
    if ([string]::IsNullOrWhiteSpace($completion)) { throw 'Cierre detenido; no hay recibo final.' }
    $resultPath = Join-Path $completion 'result.json'
    if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Falta el recibo de cierre.' }
    $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($result.status -cne 'ADMIN_SELF_ACTIVATION_OK' -or -not $result.noMigratorInvoked) {
        throw 'Cierre no acreditado.'
    }
    Write-Host "ADMIN_SELF_ACTIVATION_OK; evidencia: $completion"
}
```

El script solicitó las credenciales de `postgres` y `controlplus_test_admin` con entrada oculta, solo para lectura. La ejecución del operador informó `ADMIN_SELF_ACTIVATION_OK` en `C:\Users\luife\ControlPlus-Reviews\AdminSelf-Completion-20261105T211139Z-aeb09173414d40c1b2c8bdbc000f56e0`; el SHA-256 comunicado de `result.json` es `1C7733FDF55995480BB41EA003D9B239711BD351718C3585B8A052CD05A06978`. No repetir el bloque de aplicación real ni este cierre histórico. El agente no abrió el recibo ni consultó la base real.

## Arranque posterior

El operador inició la API y comunicó HTTP 200 para `GET /api/health`, `GET /api/health/database` y `GET /openapi/v1.json`. Estas respuestas acreditan el arranque, la comprobación de conexión y la publicación de OpenAPI; no sustituyen pruebas funcionales de anulación de venta, cierre de caja con diferencia ni cancelación de apartado por Supervisor. El agente no hizo llamadas a la API ni a `controlplus`.
