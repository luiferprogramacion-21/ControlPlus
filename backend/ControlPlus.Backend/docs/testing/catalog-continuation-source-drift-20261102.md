# Continuación detenida: el origen cambió desde el respaldo

Evidencia leída, sin conexión a PostgreSQL:
`C:\Users\luife\ControlPlus-Reviews\CatalogCloneContinuation-238048ef5c7b4dd88da829d63bdd48d2`.
No se modificaron esos archivos, el respaldo ni el clon. El operador acreditó
nativamente `INVENTORY_CHECK_EQUIVALENCE_OK checks=39` y
`EXISTING_CLONE_PLAN_OK checks=23`; no se repitieron esas pruebas.

## Comparación exacta

`failure.json` registra `RevalidateRestore`, a las
`2026-11-02T20:30:47.1904303Z`. El script conservado corresponde al actual
`Resume-ExistingCatalogClone.ps1` (mismo texto, salvo terminación de línea).

- Llamada: primera iteración, origen, de las líneas 135–137 del plan.
- Función: `Assert-CurrentBridge`, línea 95; `Assert-CatalogSnapshot -Mode Restore`.
- Esperado: `CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c\source-before.jsonl`.
- Obtenido: `CatalogCloneContinuation-238048ef5c7b4dd88da829d63bdd48d2\source-raw-compatible.jsonl`.
- Este último procede de `source-current.jsonl`, quitando únicamente el campo
  aditivo `relationDetails` en memoria. Su digest se verificó antes.

Hay **199 registros por lado**, sin claves adicionales ni ausentes. Cambiaron
**4 claves y 6 campos**. Los otros 195 registros coinciden:

| Clave | Campo | Esperado | Obtenido |
|---|---|---|---|
| `product:01a179ab-2976-74b4-a230-16535087aae1` | `version` | 3 | 4 |
| La misma clave de producto | `hash` | `4e1c662cfd8b35a7d34cc499955ae83a3776457db5aaef2d4fe1c921a3595aaa` | `e5a23741b93db4473cb44a2e9fd89590c9539c7acb64cd631270d8614a655c70` |
| `table:auditoria.evento_auditoria` | `count` | 94 | 98 |
| La misma tabla de auditoría | `hash` | `7493fbe27caeb7442a93956332a76f86e8c46dda0ebfb2c6cf039e596714488a` | `186d2f316ce797d4873f8b7e78c7e0f89cdcbc830d9ed28107809bc05c06eec0` |
| `table:catalogo.producto` | `hash` | `8464ab1c52e9be3fed9ce9a82987efd39353dbc39b98c9aa8ad1c31bdb491420` | `d206a1b985d39de7ccb2d9be1a627b0f7894c058d649c91695d0299e0c22888b` |
| `table:seguridad.usuario` | `hash` | `02b05207c54fd1cc246285ebdbf9cff189c2dd25256aa98d3e6cbb5742bab1f2` | `c9b39862be5cb75eca59cb6d22e9191be358ab59428eee670fdae650f5560eea` |

Productos conserva 3 filas y Usuarios 1. La huella individual del producto
excluye `codigo_interno` y `version`: el cambio no se explica solo por el
incremento de versión. Incluye campos operativos y de modificación; las
capturas no permiten invertir el hash e identificar sus columnas. Tampoco
identifican qué columna del usuario cambió. No se atribuye a login, contraseña
o permisos sin evidencia. Auditoría presenta un aumento **neto** de cuatro
filas; el conteo no prueba por sí solo que las 94 anteriores estén intactas.

El hash de `shape:compras.movimiento_inventario` sigue siendo
`478975aebc7d81d79a2a814c344ec67d2b3f8850f6a37eb7f870ddbe72e13e37`
en ambos lados. No hay diferencia de CHECK en esta llamada. El helper actual
sí es el utilizado, pero ambos registros del puente carecen de
`relationDetails`: la rama que canoniza el CHECK no interviene. La comparación
posterior origen/clon, donde se necesita esa equivalencia, no se alcanzó.

El helper no guardó qué clave arrojó primero; la enumeración del diccionario
no permite acreditarla. El recorrido y los archivos sí fijan inequívocamente
la comparación y las cuatro diferencias. La regresión reproduce esa llamada.

## La migración no se invocó en esta continuación

El primer `SNAPSHOT_RESTORE_OK`, anterior a la contraseña, proviene de la línea
67: compara **las dos capturas históricas del origen**, no el origen actual.
La excepción posterior aparece en la primera iteración del puente. No existen
`clone-current.jsonl`, originales enriquecidos, `restore-revalidated.txt`,
preflight controlado, `clone-apply.txt` ni postflight en esta ejecución.
La llamada Apply está después del puente completo, de una segunda contraseña
(escritor del clon), de la revalidación inmediata y del preflight controlado.
No se alcanzó ninguna de esas etapas.

`clone-preflight.txt`, anterior al fallo, acredita las 12 predecesoras y el
objetivo ausente. `clone-context.json` acredita propietario
`controlplus_test_admin`, PostgreSQL 18 y lectura como postgres. No se deduce
el estado posterior a esa captura ante intervenciones externas.

El preflight del origen acreditó READ ONLY, visibilidad completa y cero otros
clientes visibles en ese instante. Eso no descarta cambios anteriores ni
escritores posteriores. El guard se detuvo correctamente; no hay defecto de
integración que justifique relajar el comparador.

## Verificaciones y conservación

`Test-RecordedContinuationDrift.ps1`: **20 comprobaciones**, salida 0 en
PowerShell portable 7.5.4. Extrae las cuatro funciones reales del plan y ejecuta
el puente sobre copias temporales de las evidencias: reproduce el rechazo antes
del enriquecimiento, demuestra que el CHECK coincide y enumera exactamente las
cuatro claves/seis campos. Además ejecuta el diagnóstico offline real y valida
sintaxis/alcance de la nueva lectura y exclusión de credenciales/payloads.
No abre bases, no invoca herramientas nativas ni modifica evidencias.

Nativa PS5.1 de esta prueba **nueva** y SQL nuevo pendientes. No se repitieron
las 39/23 aprobadas, suites de aplicación, builds ni ensayo. Helper, Snapshot,
plan de continuación, binarios y manifiesto quedan sin cambios por este análisis.

Huellas de evidencia conservada:

```text
source-current.jsonl
E277ACB8C10B89A0760AF90CD8ABAA171C8006CEEAD83767C48FE45ACF15812E
source-raw-compatible.jsonl
AD03D41C97E5041907F7E16117B99261F67BB251C1E9CD62DF06853BD34D3D17
failure.json
4988F05AED20E086280B8576B2D2B7C60DE660378F81F3F93035B498014B63BB
```

## Siguiente bloque: únicamente diagnóstico de lectura

El script verifica esas huellas y las originales. Primero enumera las
diferencias existentes offline. Con `ReadCurrent` obtiene la captura completa
del clon que faltó y la compara estrictamente con **su propia** captura
original; no necesita equivalencia entre origen y clon para ese paso.

Después consulta solo las tres tablas implicadas, la ficha del UUID afectado,
historial y contexto en ambos lados, en REPEATABLE READ READ ONLY con rollback.
Las huellas de las tres tablas y del producto deben seguir coincidiendo con
las evidencias correspondientes en la misma transacción de cada lectura; si
no, se detiene para no atribuir nuevos cambios a este fallo.

Emite nombres y hashes de campos del producto, una lista explícita de campos
operativos no secretos de usuarios, y metadatos acotados de auditoría. No
devuelve credenciales ni sellos individuales: conserva únicamente la huella
agregada de la tabla completa que ya usa Snapshot.sql. De auditoría no devuelve motivo
ni cuerpos anteriores/nuevos. Si solo cambió un campo protegido del usuario,
la causa seguirá sin determinarse: no se amplía esa consulta automáticamente.
`field-differences.json` identifica campos y eventos distintos sin exponer
valores de producto, contraseñas, tokens o cadenas de conexión.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'

& (Join-Path $repo 'scripts\catalog-references\Test-RecordedContinuationDrift.ps1') `
    -Repository $repo

& (Join-Path $repo 'scripts\catalog-references\Read-ContinuationDataDrift.ps1') `
    -Repository $repo `
    -Evidence 'C:\Users\luife\ControlPlus-Reviews\CatalogCloneContinuation-238048ef5c7b4dd88da829d63bdd48d2' `
    -OriginalEvidence 'C:\Users\luife\ControlPlus-Backups\CatalogRehearsal-6540f35e2fae4f009387c32fe72b241c' `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin' `
    -ReadRole 'postgres' `
    -ReadCurrent
```

Solicita la contraseña oculta; crea una carpeta nueva con ACL restringida
`%USERPROFILE%\ControlPlus-Reviews\CatalogDataDrift-<UUID>`. Éxito:
`DATA_DRIFT_READONLY_OK`. No incluye ninguna llamada al migrador, restauración,
respaldo ni actualización de referencias. Las lecturas son observaciones;
mantener los escritores detenidos evita más divergencias.

## Punto seguro y continuación posterior

No volver a ejecutar `Resume-ExistingCatalogClone.ps1 -ContinueClone`: su
requisito de origen idéntico al respaldo dejó de cumplirse y se conserva.
No copiar capturas nuevas encima de las originales ni aceptar solo hashes
nuevos para pasar el guard.

Primero revisar el diagnóstico focalizado y comprobar el clon completo. Si
está intacto, el clon conservado puede seguir sirviendo para ensayar **el
respaldo antiguo**, mediante un plan separado que registre explícitamente la
deriva del origen y no afirme que el origen actual fue restaurado/verificado.
Ese plan no debe debilitar la conservación del clon respecto a su original;
su escritura sigue detenida hasta cerrar este diagnóstico y revisar el plan.
Un ensayo que pretenda acreditar los datos actuales necesitaría un respaldo y
clon nuevos, por separado, conservando todos los anteriores. Ninguna de esas
alternativas se ejecuta ni se inicia automáticamente en este bloque.
