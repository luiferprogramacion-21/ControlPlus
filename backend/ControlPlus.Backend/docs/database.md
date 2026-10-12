# Base de datos

## Fuente oficial

La fuente de verdad es `docs/ControlPlus_Fase4_Documento_y_Complementos.zip` desde la raíz del repositorio. El ZIP contiene el MER, el diccionario y `Script_Base_de_Datos_PostgreSQL_V3.sql`; Infrastructure lo incrusta y la migración base ejecuta el SQL original sobre una base vacía.

## Migraciones y seed

- `20260912010000_OfficialPhase4Baseline`: crea los siete esquemas, 58 tablas, claves, restricciones, índices, vistas y funciones aprobados.
- `20260912011000_SeedApprovedSecurityCatalog`: crea idempotentemente Administrador, Supervisor y Cajero y sus límites de descuento 100, 20 y 5.
- `20260912012000_HybridPermissionsV1`: agrega las excepciones individuales de permisos, crea las plantillas V1 y ajusta el límite de Administrador a 80 %, sin modificar la migración base.
- `20260913010000_SeedMeasurementUnitsV1`: registra idempotentemente las unidades V1 Unidad, Paquete y Metro requeridas para crear productos.
- `20260913020000_CashRegisterModuleV1`: migración publicada y aplicada una vez a la base PostgreSQL persistente local/de desarrollo; agrega `diferencia_total`, detalle inmutable de arqueo y `autorizacion_cierre_turno`, restricciones diferibles y los medios iniciales. Conserva `turno_caja.diferencia = efectivo_contado - efectivo_esperado` y permite varios pagos por movimiento para pagos combinados.
- `20260930010000_SeedInventoryAdjustmentReasonsV1`: migración publicada en código y aplicada en PostgreSQL persistente local; siembra para cada establecimiento existente los cuatro motivos aprobados de tipo `AJUSTE_INVENTARIO` sin modificar el esquema. Su reversión conserva todos los datos del catálogo y sus referencias; un `Up` posterior es idempotente por establecimiento, tipo y código.
- `20261005010000_CustomerSearchIndexesV1`: migración publicada en código y aplicada exactamente una vez en la base PostgreSQL persistente local/de desarrollo; agrega únicamente los índices de prefijo de `catalogo.cliente` para `lower(nombre_completo)`, `numero_documento`, `lower(telefono)` y el listado por estado/nombre/ID. No modifica tablas, columnas ni datos.

Las cantidades de producto, venta, compra e inventario permanecen enteras, incluidos los metros, de acuerdo con el documento principal y el script PostgreSQL V3 aprobados.

La administración del consecutivo de Ventas reutiliza `configuracion.consecutivo_documento` sin cambiar el esquema. El registro usa `tipo_documento = 'VENTA'`, la instalación activa y su serie; la unicidad oficial `(instalacion_id, tipo_documento, serie)` impide duplicados. El primer número visible se representa guardando `ultimo_numero = firstNumber - 1`; por ello la configuración inicial aprobada con `firstNumber = 1` guarda cero. Las altas y correcciones bloquean la instalación activa y la corrección usa además el token `version`. Una venta `CONFIRMADA` para la misma instalación y serie vuelve inmutable la configuración mediante la API.

El primer corte de Créditos y Abonos **no creó ninguna migración** y no modificó el script oficial, entidades oficiales ni `OfficialControlPlusDbContext`. Reutiliza `ventas.venta`, `ventas.credito`, `ventas.pago_credito`, `catalogo.cliente`, `catalogo.metodo_pago`, tablas de Caja, `seguridad.autorizacion_operacion`, auditoría y `configuracion.consecutivo_documento`. El crédito conserva la numeración `VENTA`; no tiene consecutivo independiente. Cada fila de abono usa el tipo `PAGO_CREDITO` para la instalación y serie activas, con prefijo obligatorio `ABO`. La configuración inicial aprobada `firstNumber = 1` guarda `ultimo_numero = 0`; antes del primer pago pueden corregirse números iniciales positivos con versión vigente. La presencia de cualquier `pago_credito` para instalación y serie impide nuevas correcciones. **ABO aún no está configurado en PostgreSQL persistente local/de desarrollo**. Su configuración operativa después de publicar y desplegar exige una autorización independiente; el servicio no la inserta automáticamente y, si falta, rechaza el abono con `409` sin escrituras.

Las migraciones de unidades, Caja, Inventario y Clientes están aplicadas y registradas en la base PostgreSQL persistente local/de desarrollo. No modifican `20260912010000_OfficialPhase4Baseline` ni convierten columnas `integer` a tipos decimales.

La migración de Caja usa `numeric(18,0)` para los importes nuevos y no modifica ninguna migración anterior. Antes de DDL comprueba, bajo bloqueo, que `caja.turno_caja` esté vacía. Fue validada en PostgreSQL 17 aislado y aplicada una vez de forma controlada a la base PostgreSQL persistente local/de desarrollo; no debe ejecutarse otra vez ni revertirse en esa instalación. Cualquier operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica.

La diferencia por medio y el total tienen signo contado menos esperado. `diferencia` representa exclusivamente efectivo; `diferencia_total` coincide con la suma del desglose y decide la autorización. Los negativos electrónicos son válidos; el efectivo físico no puede ser negativo. Los detalles, el vínculo y la autorización consumida quedan protegidos contra alteraciones posteriores. Los importes por método proceden de pagos; sus movimientos vinculados no se cuentan otra vez.

## Inmutabilidad y reversión

Las migraciones ya publicadas son inmutables: no se modifican, regeneran ni reemplazan, aunque una corrección posterior afecte su resultado. Las correcciones de esquema o de datos aprobados se realizan mediante una nueva migración compensatoria, revisada y documentada. `20260913020000_CashRegisterModuleV1` ya está aplicada una vez a la base PostgreSQL persistente local/de desarrollo; `20260930010000_SeedInventoryAdjustmentReasonsV1` ya está aplicada allí; `20261005010000_CustomerSearchIndexesV1` fue aplicada exactamente una vez en la base PostgreSQL persistente local/de desarrollo. Cualquier operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica.

`20260912012000_HybridPermissionsV1.Down` no es una reversión completa ni segura de la migración híbrida: no deshace todas las inserciones ni asignaciones de permisos introducidas en `Up`. No debe utilizarse como mecanismo de reversión de una base persistente. Toda reversión persistente exige un respaldo local verificado antes de cualquier operación y una revisión explícita de sus efectos.

`CashRegisterModuleV1.Down` conserva Efectivo, Transferencia y Nequi: no puede atribuir su creación al `Up`, que usa inserción idempotente sin sobrescribir nombres ni referencias. Antes de restaurar restricciones anteriores, `Down` rechaza datos incompatibles y evita una reversión parcial. Ninguna reversión persistente está autorizada por este bloque.

## Contextos y estrategia futura

`OfficialControlPlusDbContext` es el único contexto de ejecución: los repositorios funcionales lo usan para representar el modelo database-first oficial. `ControlPlusDbContext` se mantiene únicamente como portador del historial de migraciones publicadas y para las pruebas que aplican esa cadena; no se registra en la API para consultas o persistencia normal.

La migración base conserva el script oficial sin modificaciones. Mientras la cadena histórica use este portador, los cambios aprobados se expresan mediante migraciones compensatorias nuevas y se verifican desde una base limpia con Testcontainers. No se crea un `ModelSnapshot` parcial o artificial. Si en el futuro se incorpora un snapshot para generación asistida por EF Core, deberá representar fielmente el modelo oficial completo y validarse creando una base limpia con toda la cadena de migraciones.

La concurrencia optimista no requiere columnas nuevas: las 27 entidades oficiales cuyo diccionario define `version` como token se mapean con `IsConcurrencyToken()` y la versión se incrementa centralmente al guardar. Consulte [architecture.md](architecture.md) y [la decisión de persistencia](decisions/0001-persistencia-concurrencia-y-migraciones.md).

## Migrador controlado de la imagen API

`ControlPlusDbContext` es el contexto responsable de la cadena y de `__EFMigrationsHistory`; `OfficialControlPlusDbContext` sigue reservado para la operación normal. Un parser único selecciona servidor normal, healthcheck, preflight, migración o postflight antes de construir configuración o servidor HTTP. Solo admite cero argumentos, `--health-check`, `--migrate-to <MigrationId>`, `--preflight-to <MigrationId>`, el postflight de Clientes con `--postflight-to 20261005010000_CustomerSearchIndexesV1`, o el postflight de Inventario con `--postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 --expect-unrelated-reasons-sha256 <64-hex-sha256>`. El identificador tiene longitud de 1 a 200 y solo acepta ASCII alfanumérico o `_`; la huella acepta exactamente 64 caracteres hexadecimales ASCII. Cualquier otra forma devuelve código 2 y `Command line rejected` sin conexión ni escucha. Los tres modos controlados de base de datos exigen que el entorno contenga exactamente `CONTROLPLUS_MIGRATIONS_ENABLED=true`.

`--preflight-to 20260913020000_CashRegisterModuleV1` admite exclusivamente ese objetivo. Abre mediante Npgsql una transacción `REPEATABLE READ`, la establece `READ ONLY` antes de leer y confirma `transaction_read_only=on` al inicio y al final. Un manifiesto tipado compartido define las dos tablas, la columna, los seis índices con tabla/columnas, las nueve funciones con firma/retorno, los once triggers con tabla y las siete restricciones base con nombre, tabla, tipo, validación y columnas. El runner verifica además la cadena EF exacta, las cuatro filas anteriores de `__EFMigrationsHistory`, objetivo pendiente y ausencia de turnos. Las consultas son estáticas y parametrizadas, no usan `psql`, shell, SQL dinámico, agregaciones/comparaciones de arrays ni concatenación de tipos PostgreSQL.

El comando de preflight correspondiente era:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --preflight-to 20260913020000_CashRegisterModuleV1
```

El código `0` certifica todas las precondiciones, `2` indica que al menos una no se cumple o que gate/objetivo son inválidos y `1` indica un fallo técnico sanitizado. El preflight no aplica migraciones, no crea respaldos, no modifica datos, no hace commit y no intenta rollback de esquema. Como el objetivo ya está aplicado en la base PostgreSQL persistente local/de desarrollo, no corresponde repetir ese preflight allí.

Para Inventario, un segundo manifiesto tipado fija exactamente `20260930010000_SeedInventoryAdjustmentReasonsV1`, las cinco predecesoras terminando en Caja y los motivos `CONTEO_FISICO`, `MERMA_DANO`, `PERDIDA` y `CORRECCION_REGISTRO`, con órdenes 10/20/30/40, prefijo de UUID determinista, nombre, descripción y estado activo. También define nombre, tipo PostgreSQL, longitud cuando aplica y nulabilidad exacta para las once columnas de `configuracion.motivo_operacion`: `id`, `establecimiento_id`, `tipo_operacion`, `codigo`, `nombre`, `descripcion`, `orden_visual`, `activo`, `fecha_creacion`, `fecha_modificacion` y `version`. `--preflight-to` exige historial `5/5/0`, objetivo pendiente, esa forma de tabla, la forma de `uq_motivo_operacion_tipo_codigo`, ausencia global y por código de los motivos aprobados y ausencia de colisiones UUID. Evalúa conjuntamente el total, el desglose por código, UUID naturales incorrectos y colisiones antes de rechazar.

El preflight cuenta establecimientos y motivos ajenos y calcula sobre estos últimos una huella SHA-256 canónica. Las filas se ordenan por UUID y las once columnas se serializan con longitudes, tipos binarios, UTF-8 y cultura invariantes; se excluyen únicamente las claves aprobadas de Inventario. La salida expone solo `motivos_ajenos=<conteo>` y `motivos_ajenos_sha256=<64-hex>`, además de métricas agregadas; no imprime nombres ni contenido de las filas. El valor `motivos_ajenos_sha256` debe capturarse literalmente antes de ejecutar la migración.

El migrador trata Inventario como un objetivo de aplicación única: Caja debe ser exactamente la quinta y última migración aplicada. Rechaza cuatro predecesoras, objetivo ya aplicado, historial extra o incoherente; por ello nunca aplica Caja e Inventario en la misma ejecución. El comportamiento controlado existente de Caja se conserva.

`--postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 --expect-unrelated-reasons-sha256 <64-hex-sha256>` es un modo cerrado de solo lectura. Exige historial `6/6/0`, una sola fila del objetivo y, para `E` establecimientos, exactamente `4×E` filas aprobadas: una por código y establecimiento, UUID determinista, nombre, descripción, orden y estado activo exactos. Recalcula la huella excluyendo únicamente esos cuatro motivos aprobados y rechaza si difiere de la capturada en preflight, lo que detecta adición, eliminación o modificación de una fila ajena. Preflight y postflight no resuelven `IMigrator`, no ejecutan DDL, no llaman HTTP y no realizan escritura.

La huella compara dos instantáneas controladas; no constituye una garantía contra escrituras simultáneas antes, durante o después de cada transacción. El procedimiento controlado de Inventario exige una ventana de mantenimiento que bloquee cambios externos desde antes del preflight hasta después del postflight.

Los comandos de Inventario se conservan únicamente como referencia histórica de la aplicación controlada en la base PostgreSQL persistente local/de desarrollo:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --preflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1

$inventoryReasonsSha256 = '<valor motivos_ajenos_sha256 emitido por el preflight>'

docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --migrate-to 20260930010000_SeedInventoryAdjustmentReasonsV1

docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 `
  --expect-unrelated-reasons-sha256 $inventoryReasonsSha256
```

La migración `20260930010000_SeedInventoryAdjustmentReasonsV1` ya fue aplicada una sola vez en la base PostgreSQL persistente local/de desarrollo. Su preflight y postflight fueron satisfactorios: historial final `6/6/0`, objetivo registrado exactamente una vez, 1 establecimiento, 4/4 motivos y huella de motivos ajenos coincidente. Estos comandos no deben repetirse allí sin una evaluación y autorización nuevas.

Para los índices de búsqueda de Clientes, un tercer manifiesto tipado fija exactamente `20261005010000_CustomerSearchIndexesV1`, las seis predecesoras terminando en Inventario, las catorce columnas oficiales de `catalogo.cliente` y las definiciones exactas de los cuatro índices reservados. El preflight de solo lectura exige historial `6/6/0`, objetivo único pendiente, forma exacta de tabla e inexistencia de esos nombres en cualquier tabla del esquema `catalogo`. El migrador solo acepta Inventario como sexta y última fila aplicada y aplica exclusivamente el objetivo de Clientes. El postflight de solo lectura exige historial `7/7/0` y las cuatro definiciones exactas; no necesita huella porque esta migración no transforma datos.

Los comandos se conservan únicamente como referencia histórica de la aplicación controlada en la base PostgreSQL persistente local/de desarrollo:

```bash
CONTROLPLUS_MIGRATIONS_ENABLED=true dotnet ControlPlus.Api.dll --preflight-to 20261005010000_CustomerSearchIndexesV1
CONTROLPLUS_MIGRATIONS_ENABLED=true dotnet ControlPlus.Api.dll --migrate-to 20261005010000_CustomerSearchIndexesV1
CONTROLPLUS_MIGRATIONS_ENABLED=true dotnet ControlPlus.Api.dll --postflight-to 20261005010000_CustomerSearchIndexesV1
```

Estos comandos requirieron que Inventario figurara como sexta migración aplicada. No deben repetirse en la base PostgreSQL persistente local/de desarrollo sin una evaluación y autorización nuevas.

En la base PostgreSQL persistente local/de desarrollo, el preflight se ejecutó una sola vez con código 0: transacción `REPEATABLE READ, READ ONLY`, historial esperado/verificado/diferencia `6/6/0`, índices reservados encontrados/permitidos `0/4` y rollback confirmado. Antes de migrar se creó y verificó un respaldo externo al repositorio, conservado fuera de él: `ControlPlus_before_20261005010000_CustomerSearchIndexesV1_20261009-133250.dump`, formato `CUSTOM`, 472954 bytes, 796 entradas TOC verificadas con `pg_restore --list` y SHA-256 `368d8db08720634c982272ed9bf2d66f7a3a13e7c629754dbeabecf84e3f2c37`.

La migración se ejecutó una sola vez con código 0 y el resultado `Migration target '20261005010000_CustomerSearchIndexesV1' applied successfully.` El postflight se ejecutó una sola vez con código 0: transacción `REPEATABLE READ, READ ONLY`, historial `7/7/0`, índices `4/4/0` y rollback confirmado. Solo se recreó `controlplus-api`, con imagen `sha256:02bb2df581f8714d8125ccc865509d4b3533aba08603a6d9da2a9ed5e15d6238`; quedó running/healthy y con `RestartCount` 0. PostgreSQL conservó su identidad, estado running/healthy, `RestartCount` 0 y el volumen `controlplusbackend_controlplus_postgres_data`. La verificación HTTP final devolvió 200 para `GET /api/health`, `GET /api/health/database` y `GET /openapi/v1.json`. Inventario continúa figurando como aplicado previamente en esa misma base PostgreSQL persistente local/de desarrollo. El respaldo permanece conservado fuera del repositorio.

El runner rechaza destinos vacíos, inexistentes, ya aplicados o anteriores al último aplicado. También rechaza historiales que no sean un prefijo exacto del catálogo y secuencias pendientes incoherentes. La única operación permitida es aplicar la secuencia ascendente pendiente cuyo último elemento sea exactamente el destino solicitado. No existe una opción para aplicar genéricamente todo lo pendiente.

El `ENTRYPOINT` de la imagen es `dotnet ControlPlus.Api.dll`; por eso Compose adjunta los argumentos escritos después de `api`. El comando controlado que se utilizó para el objetivo de Caja fue:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --migrate-to 20260913020000_CashRegisterModuleV1
```

`--no-deps` impide que este comando cree o reinicie PostgreSQL; el destino autorizado debe estar disponible previamente mediante la configuración normal de Compose. El runner conserva la cadena de conexión solo en memoria y nunca registra configuración, credenciales, tokens, JWT, Master Key ni contenido de `.env`. Solo informa el identificador objetivo, resultado y mensajes sanitizados. El objetivo de Caja ya está aplicado en la base PostgreSQL persistente local/de desarrollo y el runner debe rechazar un segundo intento allí.

La API normal no llama `Migrate`, `MigrateAsync`, `EnsureCreated` ni equivalentes. No aplique la migración base sobre un esquema existente: el SQL oficial está diseñado para una base vacía. No use operaciones que borren volúmenes.

## Prueba integrada

`OfficialSchemaIntegrationTests` inicia PostgreSQL 17 con Testcontainers, aplica la cadena completa y verifica las 61 tablas resultantes, sus PK/FK restrictivas, catálogos, importes `numeric(18,0)`, cantidades enteras y concurrencia entre EF Core y PostgreSQL. Las pruebas específicas de Caja ejercitan transición, reversión, inmutabilidad, consumo de autorizaciones y rollback.

`ControlledMigrationTests` ejecuta el DLL real contra PostgreSQL 17 efímero y demuestra gates, destino obligatorio, rechazo de destinos inexistentes/aplicados/reversos, aplicación exacta y única, ausencia de servidor HTTP, no migración durante el arranque normal y sanitización de secretos. `ControlledPreflightTests` conserva la regresión completa del manifiesto de Caja. `ControlledInventoryMigrationTests` cubre las once columnas del manifiesto, incluidos rechazos individuales de `fecha_creacion`, `fecha_modificacion` y `version`; historia `5/5/0` y `6/6/0`; desglose por código y métricas UUID; aplicación única; varios establecimientos; y rechazo del postflight ante modificación, eliminación o adición de motivos ajenos. `CustomerSearchIndexMigrationTests` prueba definiciones exactas, `Up/Down/Up`, conservación de clientes y planes controlados que eligen cada índice de prefijo. `ControlledCustomerSearchMigrationTests` ejecuta el DLL real y cubre historia, estructura, aplicación única, solo lectura, ausencia de HTTP y errores sanitizados. `ControlledMigrationCommandLineTests` verifica la gramática cerrada para ambos postflight y la huella SHA-256 obligatoria solo para Inventario.

```powershell
dotnet test ControlPlus.Infrastructure.Tests
```

La prueba elimina su contenedor aislado al finalizar y nunca usa la base persistente de desarrollo.
