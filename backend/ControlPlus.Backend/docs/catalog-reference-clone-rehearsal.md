# Respaldo y ensayo del catálogo sobre un clon nuevo

**Nuevo ensayo autorizado el 2 de noviembre:** el operador cerró el diagnóstico
de deriva con `DATA_DRIFT_READONLY_OK` y confirmó una activación de producto y
tres inicios de sesión posteriores al respaldo, con evidencia en
`%USERPROFILE%\ControlPlus-Reviews\CatalogDataDrift-8f6b9b32b30e4aad923a95a7cea4672f`.
Autoriza ahora un respaldo y clon **nuevos**, usando el estado actual; el bloque
de este documento vuelve a ser el recorrido aplicable. No reutiliza ni altera
el respaldo, clon o evidencias del ensayo `6540f35e...`, la continuación
`238048ef...` o el diagnóstico posterior. No reanudar el clon antiguo con
`Resume-ExistingCatalogClone.ps1` ni ejecutar la activación persistente.

La detención histórica por el CHECK se conserva en el
[informe original](testing/catalog-clone-restore-difference-20261031.md).
La continuación posterior se detuvo correctamente por cambios de datos del
origen, según el [informe de deriva](testing/catalog-continuation-source-drift-20261102.md).
El diagnóstico de esa deriva no se repite para preparar este nuevo ensayo.

## Comprobación del recorrido vigente — 2 de noviembre

No fue necesario cambiar la integración ni el comparador. En el script vigente:

- Línea 59: carga el helper fijado con el comparador corregido y ya validado
  nativamente (39 comprobaciones; el plan del clon conservado tuvo otras 23).
- Líneas 156 y 191: ambas capturas nuevas pasan por `Save-CatalogSnapshot` y el
  `Snapshot.sql` fijado. Ambas contienen `relationDetails` de Inventario; no se
  mezclan con las capturas antiguas ni se necesita el puente histórico.
- Línea 192: `Assert-CatalogSnapshot -Mode Restore` invoca
  `Test-CatalogSnapshotRecordEqual`, que verifica los hashes crudos completos y
  reconoce únicamente las dos representaciones exactas del CHECK aprobado.
  Los demás objetos, flags y datos siguen estrictos. Expresiones desconocidas
  detienen el proceso.
- Líneas 198–204: revalida binarios/fuentes/SQL y nombre del clon antes del
  preflight controlado y de la única llamada Apply, con `controlplus_test_admin`.
  El migrador exige que el objetivo sea el primero pendiente; no aplica
  predecesoras por indicar el objetivo.
- Líneas 206–225: postflight, oráculo del DDL de barras, conservación Upgrade,
  comparación del origen final con la captura anterior al respaldo y
  revalidación de SHA-256 del dump y artefactos.

`Invoke-CloneRehearsal.ps1 -OfflineValidateOnly` terminó con salida **0** y
`CLONE_REHEARSAL_OFFLINE_OK` en PowerShell portable 7.5.4. Verificó los
**45 artefactos, 278 fuentes y 9 archivos de revisión** actuales, sintaxis,
objetivo y extracción del CHECK aprobado, sin abrir PostgreSQL ni la API.
Huellas vigentes, sin cambios en esta preparación:

```text
Invoke-CloneRehearsal.ps1
6F070D153240E5054527A44DA8B560BB0239EF366169CC1B37DC472D8C3C5C41
Maintenance.Helpers.ps1
85D060DC88D820CC28D87BF4C207A6BCD530EB394E277304ACA9110F623964F8
Snapshot.sql
0B644BED62880176330E03684005340B02F19ADCFA9507B254EC8AAB7AC82953
```

No se repitieron el diagnóstico de deriva, las focalizadas nativas ni las
suites/builds aprobados. La ejecución real del nuevo respaldo/restauración y
ensayo sigue **pendiente del bloque del operador**. La preparación offline no
acredita todavía que esa nueva restauración o migración haya pasado.

Preparado el 31 de octubre de 2026. El usuario confirmó el preflight real con
`PREFLIGHT_LECTURA_OK`: `127.0.0.1:5432/controlplus`, usuario `postgres`, READ ONLY,
visibilidad completa, 12 predecesoras incluida `SaleReceiptDataSnapshotV1`,
objetivo pendiente y sin incompatibilidades o códigos ambiguos reportados.
Es evidencia comunicada por el usuario; no se recibió un archivo de esa salida.
No acredita todavía respaldo, restauración, ensayo o aplicación persistente.

El bloque siguiente prepara y ejecuta **solo el respaldo y el ensayo en un
clon nuevo**. Mantener detenidos la API, el frontend y los demás escritores,
como exige el [procedimiento](catalog-reference-maintenance.md). No iniciar
la API contra el clon. El agente no ejecutó conexiones ni este ensayo.

## Bloque operativo Windows PowerShell 5.1

Usa `postgres` para leer y respaldar el origen y realizar lecturas de metadatos
del clon que requieren visibilidad completa. El rol **controlplus_test_admin**
crea, restaura y migra exclusivamente el clon; debe tener LOGIN, CREATEDB y no
SUPERUSER. Lo comprueba primero sobre la base administrativa **postgres**. No
se conceden permisos ni se sustituye ese rol por el de la aplicación. Si la
comprobación falla, seguir [la preparación documentada](testing.md#postgresql-local-para-integraciones).
Se solicitarán ambas contraseñas mediante entrada oculta.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\ControlPlus\backend\ControlPlus.Backend'

# Respaldo nuevo del origen y migración únicamente de un clon nuevo.
& (Join-Path $repo 'scripts\catalog-references\Invoke-CloneRehearsal.ps1') `
    -Repository $repo `
    -PgBin 'C:\Program Files\PostgreSQL\18\bin' `
    -ReadRole 'postgres'
```

El script reutiliza `Invoke-CatalogPgTool`, `Save-CatalogSnapshot`,
`Read-CatalogSnapshot`, `Assert-CatalogSnapshot`, `Invoke-CatalogSqlFile` e
`Invoke-CatalogControlledMode`. Comprueba las huellas fijadas de binarios,
PDB, deps/runtimeconfig, fuentes y SQL/helpers antes de conectar y antes de
migrar. No vuelve a compilar ni instala dependencias.

Una lectura nueva del informe antes del respaldo comprueba que el origen sigue
en el estado autorizado. Es una comprobación de mantenimiento necesaria para
el respaldo actual, no la repetición de una suite aprobada. La ventana sin
escritores sigue siendo necesaria: las observaciones no bloquean conexiones
futuras.

## Destinos y conservación

La carpeta nueva se muestra al comenzar y queda bajo
`%USERPROFILE%\ControlPlus-Backups\CatalogRehearsal-<UUID>`, fuera del repositorio,
con ACL restringida al usuario Windows. Guarda respaldo, SHA-256, TOC,
manifiesto, copia del script, resultados SQL y las instantáneas. Las
instantáneas contienen metadatos/UUID/huellas, no valores de filas ni claves.
El respaldo sí contiene los datos del negocio y queda en esa carpeta privada.

El clon tiene nombre generado `controlplus_test_<UUID>` y propietario
`controlplus_test_admin`. `createdb --template=template0` falla si ya existe;
no se utiliza `--clean`, `--create` en pg_restore, `dropdb` ni se reutiliza una
base. Se replica encoding, proveedor de locale, collate, ctype y locale ICU o
builtin del origen. Un desajuste de collation version bloquea la comparación.

La restauración usa `--exit-on-error --single-transaction --no-owner
--no-privileges`. Comprueba contenido y estructura, **no** restauración de los
propietarios, ACL ni roles globales de producción. Esos elementos quedan fuera
del ensayo de contenido; el dump conserva los metadatos originales. Este
ensayo no es una restauración de emergencia sobre la base real.

La única llamada `-Mode Apply` recibe `$clone` y `$testCredential`, precedida
del guard de nombre del clon. El helper rechaza además ese rol sobre
`controlplus`. Su objetivo está fijado a
`20261029010000_PermanentProductReferencesV1`; el migrador exige que sea la
primera pendiente con las 12 predecesoras completas, por lo que no aplica
implícitamente recibos ni otras predecesoras.

No se elimina el clon ni el respaldo, ni siquiera al fallar. Se conserva
`failure.json` cuando es posible. Un error detiene el proceso sin reintentar
la migración; conservar su estado y revisar la evidencia.

## Contraste exacto de ck_producto_formato_codigo

El postflight SQL existente mantiene su transacción de solo lectura. Comprueba
la presencia/validación del CHECK, pero eso no prueba por sí solo su predicado.
El ensayo añade una comparación exacta, exclusivamente en el clon:

1. Extrae el predicado de la fuente de la migración cuyo hash ya coincide con
   el PDB del binario fijado; guarda `approved-check.sql`.
2. Como `controlplus_test_admin`, abre una transacción específica READ WRITE
   **en ese clon exacto**, con guard de nombre y usuario antes de cualquier DDL.
   Crea una tabla **TEMP** con los mismos nombres/tipos de las tres columnas y
   el CHECK aprobado. No inserta productos ni altera objetos permanentes.
3. Compara literalmente los dos `pg_get_constraintdef(..., true)` producidos
   por el mismo PostgreSQL 18. Así ambos lados usan su normalización real de
   casts, paréntesis e IN/ANY, sin adivinar su formato ni borrar diferencias
   semánticas con expresiones regulares.
4. Guarda ambas definiciones y exige `BARCODE_DDL_MATCH_OK`; termina con
   **ROLLBACK**. La tabla temporal desaparece. Si falla, el cierre de la conexión
   también revierte esta transacción. Ninguna de estas operaciones toca el origen.

No se relaja el modo READ ONLY de los helpers habituales. Esta transacción
separada permite únicamente la comprobación mediante DDL temporal en el clon;
no es una migración adicional ni deja cambios permanentes.

El DDL aprobado corresponde al contrato:

| Estado | Regla |
|---|---|
| Sin barras | Formato NULL y generado=false |
| Barras externas | Formato NULL permitido; si existe metadato, pertenece a CODE128, EAN13, EAN8, UPC_A u OTRO |
| Barras marcadas como generadas | Barras y formato no nulos; formato histórico permitido en la lista anterior |
| Alta nueva con generación | La aplicación fija barras iguales a la referencia y CODE128; el CHECK conserva los formatos generados históricos válidos |

Por tanto, no se debe exigir CODE128 a todas las filas históricas ni convertir
barras externas por longitud. La migración cambia únicamente referencia y
versión del producto; la comparación de producto exige preservar literalmente
barras, formato, marca de generación y demás campos.

## Resultados que acreditará una ejecución correcta

| Evidencia | Condición exigida |
|---|---|
| `controlplus-before-catalog.dump`, `dump.sha256`, `dump.toc` | Dump nuevo no vacío; SHA-256 registrado y revalidado antes de restaurar y al terminar; TOC legible |
| `source-before.jsonl` / `source-after-backup.jsonl` | Origen sin cambios entre las lecturas que rodean el respaldo |
| `clone-restored.jsonl`, `restore-verified.txt` | `SNAPSHOT_RESTORE_OK`: todas las filas y estructuras/secuencias comparadas coinciden con el origen |
| `clone-controlled-preflight.txt` | Objetivo primero pendiente, historial exacto, READ ONLY y rollback confirmado |
| `clone-apply.txt` | Proceso controlado termina con salida 0; una única migración objetivo en el clon |
| `clone-postflight.txt` | `POSTFLIGHT_SQL_OK`: forma/unicidad P-, alias al UUID correcto, PK/FK/checks validados, índice válido, funciones/triggers y secuencia coherente |
| `clone-barcode-check.txt` | `BARCODE_DDL_MATCH_OK`: CHECK real idéntico al DDL aprobado normalizado por PostgreSQL |
| `clone-migrated.jsonl`, `upgrade-verified.txt` | `SNAPSHOT_UPGRADE_OK`: mismos UUID y número de productos, versión +1, demás campos (stock, costos, barras y relaciones) intactos; alias exactos de códigos anteriores y referencias nuevas |
| Comparación de las demás tablas | Conteos y huellas intactos de movimientos, historial, auditoría, documentos e instantáneas de idempotencia; historial EF anterior conservado más una fila del objetivo, total 13 |
| `source-after-rehearsal.jsonl` | Origen conserva datos, esquema, secuencias e historial; objetivo sigue pendiente allí |
| `result.json`, consola `CLONE_REHEARSAL_OK` | Todas las comprobaciones anteriores completadas y respaldo/clon conservados |

Las comparaciones usan `Snapshot.sql` y sus reglas existentes, sin sustituir
la auditoría de negocio ni modificar instantáneas históricas. Las huellas
detectan diferencias, no sustituyen al respaldo ni prueban ausencia de cambios
transitorios externos durante una ventana que no se respete.

## Validación histórica de la preparación inicial (31 de octubre)

Este registro conserva las huellas y conteos de aquel momento. La sección del
2 de noviembre al inicio documenta las huellas vigentes y la nueva autorización.

- `Test-ClonePlanOffline.ps1`: **23 comprobaciones** aprobadas en PowerShell
  portable 7.5.4: sintaxis, rechazo de destinos reales/malformados, ambas llamadas
  controladas limitadas al clon con el rol de pruebas, flags de restauración,
  ausencia de borrado, guard del oráculo y comparación de snapshots.
- `Invoke-CloneRehearsal.ps1 -OfflineValidateOnly`: salida 0 y
  `CLONE_REHEARSAL_OFFLINE_OK`. Comprueba sin conectar la sintaxis, 45 huellas de
  artefactos, 278 fuentes/inventario, 8 archivos de revisión (incluido el nuevo
  script operativo fijado), objetivo exacto y
  extracción del CHECK aprobado. La operación completa Windows y el SQL nuevo
  quedan pendientes del ensayo que ejecutará el usuario.
- Se detectó y conservó una modificación existente del helper: excluye
  `PGOPTIONS` ambiente solo del hijo .NET. La conexión de Preflight conserva
  su `Options` READ ONLY explícito; Apply no hereda opciones accidentales.
  Se reconstruyó en memoria la versión sin ese cambio y su SHA-256 coincidió
  exactamente con la huella anterior `0DEEDE969FBDABFBC3BA2A1F7ECA1F201C20296DFDA5B98B302323B758C470C5`.
  El manifiesto actual ya fija la nueva huella
  `6F0CC4403C26E0ACEB0C5C5A8B6F0B8C8EF449621C4898594626C257298798B8`.
  Su `Test-PgOptionsOffline.ps1` existente aprobó **18 comprobaciones** en el host
  portable. Aunque su marcador contiene WINDOWS_PS51, esa ejecución fue Linux
  7.5.4: no se atribuye una validación nativa ni se ejecutó su prueba PostgreSQL.
- No se repitieron las suites aprobadas de aplicación ni builds. No se modificó
  backend, migración, SQL/helpers existentes ni datos persistentes durante esta
  preparación. Se conservaron los cambios existentes del workspace.

La aplicación real continúa fuera de este bloque y requiere revisión de los
resultados del ensayo y autorización específica posterior. El intento de alta
pendiente no se elimina, no se cambia su clave y no se vuelve a enviar.
