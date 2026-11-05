# Aplicación del catálogo reutilizando el ensayo verificado

**Aplicación real completada por el operador el 2 de noviembre de 2026.** Se
revisó CATALOG_ACTIVATION_OK de `CatalogActivation-54e8a95787bd408aa2e13e1ee8e47333`,
historial 13 y conservación conforme. **No repetir el bloque de aplicación.**
El siguiente paso es [arranque y comprobación operativa](catalog-reference-startup.md).
El procedimiento inferior se conserva como registro de lo ejecutado; el agente
no abrió PostgreSQL ni ejecutó migraciones. No volver
a ejecutar el activador antiguo: exige otro ensayo y un helper obsoleto. Se
conserva en [el registro histórico](testing/catalog-activation-original-plan-20261031.md)
y no se modifica su script.

## Evidencias comprobadas

Carpeta:
`C:\Users\luife\ControlPlus-Backups\CatalogRehearsal-22eebc2bb624496eae905222f0fec269`.
Clon conservado: `controlplus_test_7d06a60beb414b0ebd9de7d5aaf4f0a1`.

- `result.json`: CLONE_REHEARSAL_OK, origen controlplus, sourceUnchanged=true,
  objetivo correcto, UTC `2026-11-02T20:49:55.6684890Z`.
- Respaldo **488930 bytes**, SHA-256 comprobado contra archivo, dump.sha256 y
  result.json: `E25D5D1E650DD408C9642325B66AD40CEA0A2BB3715EE2C35A9587911EC9681C`.
- Las tres capturas del origen son idénticas, SHA-256
  `E277ACB8C10B89A0760AF90CD8ABAA171C8006CEEAD83767C48FE45ACF15812E`.
- Restauración acreditada por marcador y comparación offline de los archivos
  conservados: 199 registros, 62 tablas, 3 productos, 12 predecesoras.
- Aplicación exitosa al clon y postflight acreditados: 215 registros, 63 tablas,
  3 productos, 8 alias e historial de 13. El preflight controlado declaró READ
  ONLY y rollback. No se repitió la migración ni la restauración.
- Comparación Upgrade de los archivos conservados aprobada. El CHECK de barras
  del clon coincide exactamente con el DDL aprobado, según el oráculo TEMP con
  rollback ya ejecutado. El manifiesto conservado coincide con el actual.

Las huellas de los **22 archivos originales** están fijadas en
[el recibo de revisión](testing/catalog-activation-approved-rehearsal-20261102.json),
SHA-256 `4834C7F7AC4389EAAE7A5202C900D21472EC9AD72326178514BB50608EE66239`.
El activador verifica ese recibo y cada archivo, incluido el dump; no acepta
reemplazar capturas para sortear una diferencia.

## Bloque único — Windows PowerShell 5.1

Mantener API, frontend y todos los demás escritores/migradores detenidos durante
la ventana completa. PostgreSQL permanece encendido. `MaintenanceConfirmed`
declara esa condición; no detiene procesos ni servicios. El destino está fijado
en los helpers a **127.0.0.1:5432/controlplus**.

Rol requerido: **postgres**, autorizado para DDL y con visibilidad completa.
Se solicita su contraseña **una vez mediante entrada oculta**. No utiliza el
rol de la aplicación ni controlplus_test_admin, no cambia permisos/contraseñas
y no guarda credenciales en archivos ni en argumentos del proceso.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'
$evidence = 'C:\Users\luife\ControlPlus-Backups\CatalogRehearsal-22eebc2bb624496eae905222f0fec269'

# Prueba nueva del orquestador: datos simulados; no conecta a PostgreSQL.
& (Join-Path $repo 'scripts\catalog-references\Test-VerifiedCatalogActivationOffline.ps1') `
    -Repository $repo `
    -RehearsalEvidence $evidence

# Aplicación única, tras validar respaldo, estado actual y ventana sin escritores.
& (Join-Path $repo 'scripts\catalog-references\Invoke-VerifiedCatalogActivation.ps1') `
    -Repository $repo `
    -RehearsalEvidence $evidence `
    -ExpectedBackupSha256 'E25D5D1E650DD408C9642325B66AD40CEA0A2BB3715EE2C35A9587911EC9681C' `
    -DatabaseRole 'postgres' `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin' `
    -MaintenanceConfirmed
```

La prueba nueva detiene el bloque si falla. No repite las suites de aplicación,
el diagnóstico de deriva ni el ensayo. Solo usa fixtures temporales y las
capturas conservadas. No aplicar el bloque antiguo ni invocar manualmente EF.

Los marcadores Restore/Upgrade anteriores a la contraseña verifican únicamente
archivos históricos, como anuncia el script. El origen vivo se comprueba
después de introducir las credenciales; esos marcadores iniciales no lo certifican.

## Controles antes de Apply

1. Verifica integridad de recibo, 22 archivos y respaldo. Revalida Restore y
   Upgrade sobre **archivos existentes**, sin crear otro respaldo, restaurar,
   conectar al clon ni migrarlo otra vez.
2. Reutiliza únicamente el modo OfflineValidateOnly del procedimiento de ensayo
   para comprobar 45 artefactos, 278 fuentes y 9 revisiones vigentes. Exige el
   mismo manifiesto que el ensayo. No compila ni restaura dependencias.
3. Crea una carpeta nueva privada
   `%USERPROFILE%\ControlPlus-Reviews\CatalogActivation-<UUID>`, con ACL limitada
   al operador. Copia los 45 artefactos fijados a activation-bin y verifica sus
   hashes. No sobrescribe binarios ni evidencias anteriores.
4. Preflight SQL READ ONLY: identidad del destino/usuario/session_user,
   PostgreSQL 18, visibilidad completa, cero otros clientes, las **12
   predecesoras exactas**, SaleReceiptDataSnapshotV1 incluida, objetivo pendiente
   y ausencia de incompatibilidades/reservas/códigos ambiguos. Una visibilidad
   parcial detiene, no acredita ausencia de otros clientes.
5. Captura actual completa e historial: debe coincidir por Restore con
   source-before.jsonl del respaldo revisado. Cualquier cambio de datos,
   estructura, secuencia o historial no admitido por el comparador detiene.
6. Preflight controlado .NET con la copia fijada: READ ONLY/rollback, sin HTTP.
   Toma otra captura inmediata completa, exige igualdad con el respaldo y
   verifica otra vez hashes. Como última lectura antes de Apply comprueba de
   nuevo el informe y la ausencia de otros clientes.
7. Solo entonces invoca Apply **una vez**, con gate temporal y objetivo único
   `20261029010000_PermanentProductReferencesV1`. El corredor exige que sea la
   primera pendiente tras las doce; no aplica predecesoras automáticamente.

Los controles reutilizan los helpers y SQL fijados. El comparador reconoce
únicamente las dos representaciones aprobadas del CHECK de Inventario y
conserva todos los demás datos/objetos/flags. La ventana sigue siendo necesaria:
las lecturas son observaciones, no un bloqueo contra conexiones futuras o
cambios transitorios entre lecturas. El estado actual solo quedará acreditado
cuando el operador ejecute el bloque, no por las evidencias históricas.

## Comprobaciones posteriores y éxito

- Postflight.sql READ ONLY y `POSTFLIGHT_SQL_OK`: forma y unicidad de referencias,
  alias y barras enlazados al UUID correcto, registro, restricciones, índices,
  funciones, triggers y secuencia coherentes.
- Consulta READ ONLY del CHECK de barras real: validado y exactamente igual al
  predicado aprobado que se acreditó en el ensayo. No crea TEMP ni realiza DDL
  adicional en la base real.
- Historial exacto 13, objetivo una sola vez. Snapshot Upgrade conserva UUID,
  cantidades, costos, barras y metadatos, relaciones, auditoría, movimientos,
  historial e instantáneas de idempotencia. Solo admite referencia/versión y
  objetos previstos por la migración.
- Snapshot MigratedCopy exige además coincidencia con el clon ya migrado; solo
  tolera la diferencia prevista de fecha de alta de los alias, manteniendo
  cantidad y correspondencia exacta. Verifica nuevamente hashes de respaldo,
  evidencias y binarios congelados.

Éxito final: **CATALOG_ACTIVATION_OK**, con result.json y ruta de evidencias
nuevas. Ese archivo registra respaldo reutilizado, SHA-256, clon verificado,
historial 12→13 y DLL compatible congelada. El script no inicia la API.

Ante cualquier fallo, conservar todo y mantener la API detenida. Si Apply fue
invocado, **no asumir rollback ni reejecutar**: revisar historial/esquema en
lectura antes de decidir. No hay reintentos, Down, restauración persistente ni
limpieza automática. No se repite el alta pendiente ni se altera su clave.

## Verificación de la preparación

- `Invoke-VerifiedCatalogActivation.ps1 -OfflineValidateOnly`: salida **0**, en
  PowerShell portable 7.5.4; valida las evidencias reales, respaldo, manifiesto,
  artefactos/fuentes y comparaciones conservadas. Sin PostgreSQL ni API.
- `Test-VerifiedCatalogActivationOffline.ps1`: **52 comprobaciones**, salida 0
  en portable 7.5.4. Ejecuta el try/catch/finally real con fronteras de IO
  simuladas y comparadores reales: éxito, deriva inicial/inmediata, visibilidad
  parcial, otros clientes, objetivo aplicado, artefactos, respuesta incierta,
  CHECK distinto, alias faltante, limpieza de credenciales y ausencia de éxito
  falso/reintento. Rechaza también backup/capturas/result alterados y otro SHA.
- La validación nativa del **orquestador nuevo** queda incluida en el primer
  comando del bloque. Los helpers, comparador y ensayo ya tenían evidencia
  nativa. No se repitieron suites/builds aprobados ni el ensayo de base.

La ejecución real y su postflight quedaron acreditados en la revisión posterior
del 2 de noviembre enlazada al inicio. El arranque/HTTP/visual siguen pendientes.
Sin commit ni push. El procedimiento histórico se conserva aparte.
