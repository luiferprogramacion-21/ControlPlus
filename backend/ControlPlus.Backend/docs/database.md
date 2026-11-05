# Base de datos

## Catálogo: referencias permanentes, preparado sin aplicación real

El 30 de octubre de 2026 se comprobaron los TRX Windows de Catálogo: migraciones
**6/6**, API **49/49** y Domain **70/70**, sin errores ni omisiones. La migración
`20261029010000_PermanentProductReferencesV1` continúa **sin aplicar a la base
real**. [Evidencia](testing/catalog-references-20261029.md) y
[procedimiento PowerShell 5.1 para revisión](catalog-reference-maintenance.md).

El objetivo tiene una protección adicional en el migrador: debe ser la primera
pendiente, con las doce predecesoras aplicadas, incluida
`20261028010000_SaleReceiptDataSnapshotV1`. No debe confundirse con el
comportamiento genérico que sí puede ejecutar una cadena hasta el destino.
Una predecesora faltante exige revisión y autorización separadas. El historial
real no se consultó durante esta preparación. No existe postflight CLI de
Catálogo: se prepara SQL de solo lectura y huellas de conservación, sin
ejecutarlo. Respaldo, restauración temporal, ensayo, aplicación y postflight
persistentes siguen pendientes de revisión y autorización.

## Fuente oficial

La fuente de verdad es `docs/ControlPlus_Fase4_Documento_y_Complementos.zip` desde la raíz del repositorio. El ZIP contiene el MER, el diccionario y `Script_Base_de_Datos_PostgreSQL_V3.sql`; Infrastructure lo incrusta y la migración base ejecuta el SQL original sobre una base vacía.

La instalación local V1 usa PostgreSQL 18. La base `controlplus` fue restaurada de PostgreSQL 17 a través de un respaldo lógico verificado; la comparación encontró 62 tablas, una vista y 230 filas en 18 tablas con datos. Los respaldos permanecen fuera del repositorio. La migración de Compras corte 2 se aplicó una vez de forma controlada el 16 de octubre de 2026; iniciar la API no aplica migraciones.

Antes de aplicar `20261014010000_InventoryMovementRegistrationOrderV1` a la base persistente local se creó `controlplus-pg18-before-compras-20261016-142422-be1e2bfb.dump` en `C:\Users\luife\ControlPlus-Backups`, fuera del repositorio. `pg_dump` generó 476028 bytes, `pg_restore -l` leyó 800 entradas y SHA-256 fue `999698102666D3A6E6A9A39D3FB864D741918A6022F46201E79FF2D2F8D4F0D3`. Estas comprobaciones no equivalen a una restauración de prueba. El preflight de solo lectura terminó con `PREFLIGHT_COMPRAS_OK`; la ejecución dirigida terminó con `MigrationExitCode: 0`. El postflight en `REPEATABLE READ, READ ONLY` y rollback devolvió `POSTFLIGHT_COMPRAS_OK` y valor verdadero para historial exacto, columna, secuencia, restricción, índice, funciones y disparadores. La API local respondió HTTP `200` en `/api/health`, `/api/health/database` y `/openapi/v1.json` después del cambio. El catálogo de motivos `ANULACION_COMPRA` no se inicializó por esta migración y requiere una operación explícita aparte. Esta migración no debe volver a aplicarse ni revertirse en la base persistente local sin evaluación, respaldo y autorización específica.

## Migraciones y seed

- `20260912010000_OfficialPhase4Baseline`: crea los siete esquemas, 58 tablas, claves, restricciones, índices, vistas y funciones aprobados.
- `20260912011000_SeedApprovedSecurityCatalog`: crea idempotentemente Administrador, Supervisor y Cajero y sus límites de descuento 100, 20 y 5.
- `20260912012000_HybridPermissionsV1`: agrega las excepciones individuales de permisos, crea las plantillas V1 y ajusta el límite de Administrador a 80 %, sin modificar la migración base.
- `20260913010000_SeedMeasurementUnitsV1`: registra idempotentemente las unidades V1 Unidad, Paquete y Metro requeridas para crear productos.
- `20260913020000_CashRegisterModuleV1`: migración publicada y aplicada una vez a la base PostgreSQL persistente local/de desarrollo; agrega `diferencia_total`, detalle inmutable de arqueo y `autorizacion_cierre_turno`, restricciones diferibles y los medios iniciales. Conserva `turno_caja.diferencia = efectivo_contado - efectivo_esperado` y permite varios pagos por movimiento para pagos combinados.
- `20260930010000_SeedInventoryAdjustmentReasonsV1`: migración publicada en código y aplicada en PostgreSQL persistente local; siembra para cada establecimiento existente los cuatro motivos aprobados de tipo `AJUSTE_INVENTARIO` sin modificar el esquema. Su reversión conserva todos los datos del catálogo y sus referencias; un `Up` posterior es idempotente por establecimiento, tipo y código.
- `20261005010000_CustomerSearchIndexesV1`: migración publicada en código y aplicada exactamente una vez en la base PostgreSQL persistente local/de desarrollo; agrega únicamente los índices de prefijo de `catalogo.cliente` para `lower(nombre_completo)`, `numero_documento`, `lower(telefono)` y el listado por estado/nombre/ID. No modifica tablas, columnas ni datos.
- `20261014010000_InventoryMovementRegistrationOrderV1`: aplicada una vez a PostgreSQL 18 persistente local/de desarrollo tras siete migraciones predecesoras. Agrega `compras.movimiento_inventario.orden_registro bigint NULL`, secuencia, restricción positiva, índice único parcial, dos funciones y dos disparadores; conserva `NULL` en movimientos históricos.

Las cantidades de producto, venta, compra e inventario permanecen enteras, incluidos los metros, de acuerdo con el documento principal y el script PostgreSQL V3 aprobados.

La administración del consecutivo de Ventas reutiliza `configuracion.consecutivo_documento` sin cambiar el esquema. El registro usa `tipo_documento = 'VENTA'`, la instalación activa y su serie; la unicidad oficial `(instalacion_id, tipo_documento, serie)` impide duplicados. El primer número visible se representa guardando `ultimo_numero = firstNumber - 1`; por ello la configuración inicial aprobada con `firstNumber = 1` guarda cero. Las altas y correcciones bloquean la instalación activa y la corrección usa además el token `version`. Una venta `CONFIRMADA` para la misma instalación y serie vuelve inmutable la configuración mediante la API.

El primer corte de Créditos y Abonos **no creó ninguna migración** y no modificó el script oficial, entidades oficiales ni `OfficialControlPlusDbContext`. Reutiliza `ventas.venta`, `ventas.credito`, `ventas.pago_credito`, `catalogo.cliente`, `catalogo.metodo_pago`, tablas de Caja, `seguridad.autorizacion_operacion`, auditoría y `configuracion.consecutivo_documento`. El crédito conserva la numeración `VENTA`; no tiene consecutivo independiente. Cada fila de abono usa el tipo `PAGO_CREDITO` para la instalación y serie activas, con prefijo obligatorio `ABO`. La configuración inicial aprobada `firstNumber = 1` guarda `ultimo_numero = 0`; antes del primer pago pueden corregirse números iniciales positivos con versión vigente. La presencia de cualquier `pago_credito` para instalación y serie impide nuevas correcciones. Tras la publicación del commit y el despliegue local de la imagen API, ABO quedó presente y certificado mediante la API en PostgreSQL persistente local/de desarrollo con `prefix=ABO`, `lastNumber=0`, `nextNumber=1` y `version` válida. El servicio no inserta el consecutivo automáticamente y, si falta, rechaza el abono con `409` sin escrituras. Esta operación no ejecutó migraciones, SQL directo ni cambios de esquema.

Las migraciones de unidades, Caja, Inventario, Clientes y Compras corte 2 están aplicadas y registradas en la base PostgreSQL persistente local/de desarrollo. No modifican `20260912010000_OfficialPhase4Baseline` ni convierten columnas `integer` a tipos decimales.

La migración de Caja usa `numeric(18,0)` para los importes nuevos y no modifica ninguna migración anterior. Antes de DDL comprueba, bajo bloqueo, que `caja.turno_caja` esté vacía. Fue validada en PostgreSQL 17 aislado y aplicada una vez de forma controlada a la base PostgreSQL persistente local/de desarrollo; no debe ejecutarse otra vez ni revertirse en esa instalación. Cualquier operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica.

La diferencia por medio y el total tienen signo contado menos esperado. `diferencia` representa exclusivamente efectivo; `diferencia_total` coincide con la suma del desglose y decide la autorización. Los negativos electrónicos son válidos; el efectivo físico no puede ser negativo. Los detalles, el vínculo y la autorización consumida quedan protegidos contra alteraciones posteriores. Los importes por método proceden de pagos; sus movimientos vinculados no se cuentan otra vez.

## Inmutabilidad y reversión

Las migraciones ya publicadas son inmutables: no se modifican, regeneran ni reemplazan, aunque una corrección posterior afecte su resultado. Las correcciones de esquema o de datos aprobados se realizan mediante una nueva migración compensatoria, revisada y documentada. `20260913020000_CashRegisterModuleV1` ya está aplicada una vez a la base PostgreSQL persistente local/de desarrollo; `20260930010000_SeedInventoryAdjustmentReasonsV1` ya está aplicada allí; `20261005010000_CustomerSearchIndexesV1` y `20261014010000_InventoryMovementRegistrationOrderV1` fueron aplicadas una vez en la base PostgreSQL persistente local/de desarrollo. Cualquier operación persistente futura de esquema o datos requiere evaluación, respaldo y autorización específica.

`20260912012000_HybridPermissionsV1.Down` no es una reversión completa ni segura de la migración híbrida: no deshace todas las inserciones ni asignaciones de permisos introducidas en `Up`. No debe utilizarse como mecanismo de reversión de una base persistente. Toda reversión persistente exige un respaldo local verificado antes de cualquier operación y una revisión explícita de sus efectos.

`CashRegisterModuleV1.Down` conserva Efectivo, Transferencia y Nequi: no puede atribuir su creación al `Up`, que usa inserción idempotente sin sobrescribir nombres ni referencias. Antes de restaurar restricciones anteriores, `Down` rechaza datos incompatibles y evita una reversión parcial. Ninguna reversión persistente está autorizada por este bloque.

## Contextos y estrategia futura

`OfficialControlPlusDbContext` es el único contexto de ejecución: los repositorios funcionales lo usan para representar el modelo database-first oficial. `ControlPlusDbContext` se mantiene únicamente como portador del historial de migraciones publicadas y para las pruebas que aplican esa cadena; no se registra en la API para consultas o persistencia normal.

La migración base conserva el script oficial sin modificaciones. Mientras la cadena histórica use este portador, los cambios aprobados se expresan mediante migraciones compensatorias nuevas y se verifican desde una base temporal y limpia de PostgreSQL 18 local. No se crea un `ModelSnapshot` parcial o artificial. Si en el futuro se incorpora un snapshot para generación asistida por EF Core, deberá representar fielmente el modelo oficial completo y validarse creando una base limpia con toda la cadena de migraciones.

La concurrencia optimista no requiere columnas nuevas: las 27 entidades oficiales cuyo diccionario define `version` como token se mapean con `IsConcurrencyToken()` y la versión se incrementa centralmente al guardar. Consulte [architecture.md](architecture.md) y [la decisión de persistencia](decisions/0001-persistencia-concurrencia-y-migraciones.md).

## Migrador controlado de la API local

`ControlPlusDbContext` es el contexto responsable de la cadena y de `__EFMigrationsHistory`; `OfficialControlPlusDbContext` sigue reservado para la operación normal. Un parser único selecciona servidor normal, preflight, migración o postflight antes de construir configuración o servidor HTTP. Solo admite cero argumentos, `--migrate-to <MigrationId>`, `--preflight-to <MigrationId>`, el postflight de Clientes con `--postflight-to 20261005010000_CustomerSearchIndexesV1`, o el postflight de Inventario con `--postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 --expect-unrelated-reasons-sha256 <64-hex-sha256>`. El identificador tiene longitud de 1 a 200 y solo acepta ASCII alfanumérico o `_`; la huella acepta exactamente 64 caracteres hexadecimales ASCII. Cualquier otra forma devuelve código 2 y `Command line rejected` sin conexión ni escucha. Los tres modos controlados de base de datos exigen que el entorno contenga exactamente `CONTROLPLUS_MIGRATIONS_ENABLED=true`.

`--preflight-to 20260913020000_CashRegisterModuleV1` admite exclusivamente ese objetivo. Abre mediante Npgsql una transacción `REPEATABLE READ`, la establece `READ ONLY` antes de leer y confirma `transaction_read_only=on` al inicio y al final. Un manifiesto tipado compartido define las dos tablas, la columna, los seis índices con tabla/columnas, las nueve funciones con firma/retorno, los once triggers con tabla y las siete restricciones base con nombre, tabla, tipo, validación y columnas. El runner verifica además la cadena EF exacta, las cuatro filas anteriores de `__EFMigrationsHistory`, objetivo pendiente y ausencia de turnos. Las consultas son estáticas y parametrizadas, no usan `psql`, shell, SQL dinámico, agregaciones/comparaciones de arrays ni concatenación de tipos PostgreSQL.

El preflight histórico de Caja ya fue ejecutado en la base persistente; no debe repetirse allí. Para objetivos futuros, se utiliza el ejecutable local con el gate y el destino explícitos.

El código `0` certifica todas las precondiciones, `2` indica que al menos una no se cumple o que gate/objetivo son inválidos y `1` indica un fallo técnico sanitizado. El preflight no aplica migraciones, no crea respaldos, no modifica datos, no hace commit y no intenta rollback de esquema. Como el objetivo ya está aplicado en la base PostgreSQL persistente local/de desarrollo, no corresponde repetir ese preflight allí.

Para Inventario, un segundo manifiesto tipado fija exactamente `20260930010000_SeedInventoryAdjustmentReasonsV1`, las cinco predecesoras terminando en Caja y los motivos `CONTEO_FISICO`, `MERMA_DANO`, `PERDIDA` y `CORRECCION_REGISTRO`, con órdenes 10/20/30/40, prefijo de UUID determinista, nombre, descripción y estado activo. También define nombre, tipo PostgreSQL, longitud cuando aplica y nulabilidad exacta para las once columnas de `configuracion.motivo_operacion`: `id`, `establecimiento_id`, `tipo_operacion`, `codigo`, `nombre`, `descripcion`, `orden_visual`, `activo`, `fecha_creacion`, `fecha_modificacion` y `version`. `--preflight-to` exige historial `5/5/0`, objetivo pendiente, esa forma de tabla, la forma de `uq_motivo_operacion_tipo_codigo`, ausencia global y por código de los motivos aprobados y ausencia de colisiones UUID. Evalúa conjuntamente el total, el desglose por código, UUID naturales incorrectos y colisiones antes de rechazar.

El preflight cuenta establecimientos y motivos ajenos y calcula sobre estos últimos una huella SHA-256 canónica. Las filas se ordenan por UUID y las once columnas se serializan con longitudes, tipos binarios, UTF-8 y cultura invariantes; se excluyen únicamente las claves aprobadas de Inventario. La salida expone solo `motivos_ajenos=<conteo>` y `motivos_ajenos_sha256=<64-hex>`, además de métricas agregadas; no imprime nombres ni contenido de las filas. El valor `motivos_ajenos_sha256` debe capturarse literalmente antes de ejecutar la migración.

El migrador trata Inventario como un objetivo de aplicación única: Caja debe ser exactamente la quinta y última migración aplicada. Rechaza cuatro predecesoras, objetivo ya aplicado, historial extra o incoherente; por ello nunca aplica Caja e Inventario en la misma ejecución. El comportamiento controlado existente de Caja se conserva.

`--postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 --expect-unrelated-reasons-sha256 <64-hex-sha256>` es un modo cerrado de solo lectura. Exige historial `6/6/0`, una sola fila del objetivo y, para `E` establecimientos, exactamente `4×E` filas aprobadas: una por código y establecimiento, UUID determinista, nombre, descripción, orden y estado activo exactos. Recalcula la huella excluyendo únicamente esos cuatro motivos aprobados y rechaza si difiere de la capturada en preflight, lo que detecta adición, eliminación o modificación de una fila ajena. Preflight y postflight no resuelven `IMigrator`, no ejecutan DDL, no llaman HTTP y no realizan escritura.

La huella compara dos instantáneas controladas; no constituye una garantía contra escrituras simultáneas antes, durante o después de cada transacción. El procedimiento controlado de Inventario exige una ventana de mantenimiento que bloquee cambios externos desde antes del preflight hasta después del postflight.

El preflight, la migración y el postflight de Inventario son hechos históricos y no deben repetirse en la base persistente. Los modos actuales se invocan directamente en el ejecutable local con el gate y el destino autorizados.

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

El objetivo de Caja se aplicó históricamente una sola vez. Para una migración futura autorizada, el ejecutable local acepta los mismos argumentos controlados sin iniciar HTTP.

El servicio PostgreSQL local debe estar disponible antes de cualquier comando controlado. El runner conserva la cadena de conexión solo en memoria y nunca registra configuración, credenciales, tokens, JWT, Master Key ni contenido de User Secrets. Solo informa el identificador objetivo, resultado y mensajes sanitizados. El objetivo de Caja ya está aplicado en la base PostgreSQL persistente local/de desarrollo y el runner debe rechazar un segundo intento allí.

La API normal no llama `Migrate`, `MigrateAsync`, `EnsureCreated` ni equivalentes. No aplique la migración base sobre un esquema existente: el SQL oficial está diseñado para una base vacía. No elimine la base persistente ni sus respaldos.

## Prueba integrada

`OfficialSchemaIntegrationTests` crea una base temporal en PostgreSQL 18 local, aplica la cadena completa y verifica las 61 tablas resultantes, sus PK/FK restrictivas, catálogos, importes `numeric(18,0)`, cantidades enteras y concurrencia entre EF Core y PostgreSQL. Las pruebas específicas de Caja ejercitan transición, reversión, inmutabilidad, consumo de autorizaciones y rollback.

`ControlledMigrationTests` ejecuta el DLL real contra PostgreSQL 18 en bases temporales locales y demuestra gates, destino obligatorio, rechazo de destinos inexistentes/aplicados/reversos, aplicación exacta y única, ausencia de servidor HTTP, no migración durante el arranque normal y sanitización de secretos. `ControlledPreflightTests` conserva la regresión completa del manifiesto de Caja. `ControlledInventoryMigrationTests` cubre las once columnas del manifiesto, incluidos rechazos individuales de `fecha_creacion`, `fecha_modificacion` y `version`; historia `5/5/0` y `6/6/0`; desglose por código y métricas UUID; aplicación única; varios establecimientos; y rechazo del postflight ante modificación, eliminación o adición de motivos ajenos. `CustomerSearchIndexMigrationTests` prueba definiciones exactas, `Up/Down/Up`, conservación de clientes y planes controlados que eligen cada índice de prefijo. `ControlledCustomerSearchMigrationTests` ejecuta el DLL real y cubre historia, estructura, aplicación única, solo lectura, ausencia de HTTP y errores sanitizados. `ControlledMigrationCommandLineTests` verifica la gramática cerrada para ambos postflight y la huella SHA-256 obligatoria solo para Inventario.

```powershell
dotnet test ControlPlus.Infrastructure.Tests
```

Las pruebas eliminan sus bases temporales al finalizar y nunca usan la base persistente de desarrollo.

## Inventario inicial de producto (aplicado en la base local)

La migración `20261026010000_InitialProductInventoryMovementV1` admite
`INVENTARIO_INICIAL`, añade una restricción para sus cantidades, costos y
referencias, y un índice único por producto. No modifica movimientos históricos.
Su `Down` rechaza la reversión si ya existen movimientos iniciales. El migrador
exige que las diez predecesoras estén aplicadas y que este objetivo sea el primero
pendiente. El preflight controlado comprueba ese historial, la restricción oficial
y la ausencia de los dos nombres reservados en una transacción
`REPEATABLE READ, READ ONLY` que termina con rollback. Las pruebas focalizadas
previas figuran en [testing.md](testing.md).

El DDL y la fila de historial se confirman en la transacción de EF Core; un error
durante esa transacción los revierte juntos. El corredor también comprueba el
historial después del commit. Si falla esa comprobación o se pierde la conexión
durante el commit, el código de fallo no demuestra que la migración se haya
revertido: su estado queda incierto y debe verificarse en solo lectura antes de
decidir cualquier reintento, sin volver a ejecutar el destino automáticamente.

El 28 de octubre de 2026 el usuario informó que el preflight y el respaldo
terminaron correctamente antes de la aplicación dirigida en
`127.0.0.1:5432/controlplus`. Se usó HEAD
`5ec17b6e088138499edaf74afbdbaec4aa601f94` y la DLL Release cuya huella
SHA-256 fue `8758826772CC64B7BC240460FD4AB96E37855F45207394D526D296C567859EDC`.
El respaldo externo al repositorio es
`C:\Users\luife\ControlPlus-Backups\InitialProductInventory-20261028-011142-5178d185f0a84f6088df56fd0827c82a\controlplus.dump`:
485285 bytes y SHA-256
`3AA48A6923931A3040FE381F7B63597D4CCBC4274B3A1902F91039AC295DAF22`.
La comprobación registrada del respaldo incluyó tamaño positivo y catálogo
legible; esto no equivale a una restauración de prueba.

El script externo de aplicación informó éxito del migrador, pero falló su
consulta de postflight. Una comprobación posterior **independiente**, de solo
lectura, sí confirmó historial exacto de once migraciones, las dos restricciones
validadas, el índice único parcial válido y cero movimientos
`INVENTARIO_INICIAL`. El historial real usa `public."__EFMigrationsHistory"` y
la columna `"MigrationId"`. El SQL del script externo
`Apply-InitialProductInventory.ps1` debe corregir sus dos consultas de historial:
`json_agg(migration_id ORDER BY migration_id)` por
`json_agg("MigrationId" ORDER BY "MigrationId")`. El fallo de esa consulta no
invalida el éxito del migrador ni reemplaza la comprobación independiente.

Esta migración ya está aplicada en la base persistente local: no repetirla ni
intentar su reversión automática. La API normal no aplica migraciones.
El contrato de alta se describe en [product-initial-stock-design.md](product-initial-stock-design.md).
# Acceso a códigos de producto (2 de noviembre de 2026)

La migración compensatoria `20261102010000_GrantProductCodeAccessV1` fue aplicada una vez a `controlplus` por el operador el 2 de noviembre de 2026, con respaldo verificado y marcador `PRODUCT_CODE_GRANT_OK`. Concede a `controlplus_app` los permisos requeridos sobre los objetos nuevos de referencias permanentes. **No repetirla.** Evidencia y procedimiento histórico: [product-code-access.md](product-code-access.md).
