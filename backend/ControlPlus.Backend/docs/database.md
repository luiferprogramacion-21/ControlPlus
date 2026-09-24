# Base de datos

## Fuente oficial

La fuente de verdad es `docs/ControlPlus_Fase4_Documento_y_Complementos.zip` desde la raíz del repositorio. El ZIP contiene el MER, el diccionario y `Script_Base_de_Datos_PostgreSQL_V3.sql`; Infrastructure lo incrusta y la migración base ejecuta el SQL original sobre una base vacía.

## Migraciones y seed

- `20260912010000_OfficialPhase4Baseline`: crea los siete esquemas, 58 tablas, claves, restricciones, índices, vistas y funciones aprobados.
- `20260912011000_SeedApprovedSecurityCatalog`: crea idempotentemente Administrador, Supervisor y Cajero y sus límites de descuento 100, 20 y 5.
- `20260912012000_HybridPermissionsV1`: agrega las excepciones individuales de permisos, crea las plantillas V1 y ajusta el límite de Administrador a 80 %, sin modificar la migración base.
- `20260913010000_SeedMeasurementUnitsV1`: registra idempotentemente las unidades V1 Unidad, Paquete y Metro requeridas para crear productos.
- `20260913020000_CashRegisterModuleV1`: migración no publicada de Caja; agrega `diferencia_total`, detalle inmutable de arqueo y `autorizacion_cierre_turno`, restricciones diferibles y los medios iniciales. Conserva `turno_caja.diferencia = efectivo_contado - efectivo_esperado` y permite varios pagos por movimiento para pagos combinados.

Las cantidades de producto, venta, compra e inventario permanecen enteras, incluidos los metros, de acuerdo con el documento principal y el script PostgreSQL V3 aprobados.

La migración de unidades está aplicada y registrada en la base persistente de desarrollo. No modifica `20260912010000_OfficialPhase4Baseline` ni convierte columnas `integer` a tipos decimales.

La migración de Caja usa `numeric(18,0)` para los importes nuevos y no modifica ninguna migración publicada. Antes de DDL comprueba, bajo bloqueo, que `caja.turno_caja` esté vacía. Si hay un turno abierto o cerrado, requiere una migración de transición específica y aborta sin adaptar ni borrar datos históricos. Se prueba exclusivamente en PostgreSQL 17 aislado; no se aplica a la base persistente en este bloque.

La diferencia por medio y el total tienen signo contado menos esperado. `diferencia` representa exclusivamente efectivo; `diferencia_total` coincide con la suma del desglose y decide la autorización. Los negativos electrónicos son válidos; el efectivo físico no puede ser negativo. Los detalles, el vínculo y la autorización consumida quedan protegidos contra alteraciones posteriores. Los importes por método proceden de pagos; sus movimientos vinculados no se cuentan otra vez.

## Inmutabilidad y reversión

Las cuatro primeras migraciones están publicadas y son inmutables. No se modifican, regeneran ni reemplazan, aunque una corrección posterior afecte su resultado. Las correcciones de esquema o de datos aprobados se realizan mediante una nueva migración compensatoria, revisada y documentada. `20260913020000_CashRegisterModuleV1` permanece pendiente de revisión y autorización antes de cualquier aplicación persistente.

`20260912012000_HybridPermissionsV1.Down` no es una reversión completa ni segura de la migración híbrida: no deshace todas las inserciones ni asignaciones de permisos introducidas en `Up`. No debe utilizarse como mecanismo de reversión de una base persistente. Toda reversión persistente exige un respaldo local verificado antes de cualquier operación y una revisión explícita de sus efectos.

`CashRegisterModuleV1.Down` conserva Efectivo, Transferencia y Nequi: no puede atribuir su creación al `Up`, que usa inserción idempotente sin sobrescribir nombres ni referencias. Antes de restaurar restricciones anteriores, `Down` rechaza datos incompatibles y evita una reversión parcial. Ninguna reversión persistente está autorizada por este bloque.

## Contextos y estrategia futura

`OfficialControlPlusDbContext` es el único contexto de ejecución: los repositorios funcionales lo usan para representar el modelo database-first oficial. `ControlPlusDbContext` se mantiene únicamente como portador del historial de migraciones publicadas y para las pruebas que aplican esa cadena; no se registra en la API para consultas o persistencia normal.

La migración base conserva el script oficial sin modificaciones. Mientras la cadena histórica use este portador, los cambios aprobados se expresan mediante migraciones compensatorias nuevas y se verifican desde una base limpia con Testcontainers. No se crea un `ModelSnapshot` parcial o artificial. Si en el futuro se incorpora un snapshot para generación asistida por EF Core, deberá representar fielmente el modelo oficial completo y validarse creando una base limpia con toda la cadena de migraciones.

La concurrencia optimista no requiere columnas nuevas: las 27 entidades oficiales cuyo diccionario define `version` como token se mapean con `IsConcurrencyToken()` y la versión se incrementa centralmente al guardar. Consulte [architecture.md](architecture.md) y [la decisión de persistencia](decisions/0001-persistencia-concurrencia-y-migraciones.md).

## Migrador controlado de la imagen API

`ControlPlusDbContext` es el contexto responsable de la cadena y de `__EFMigrationsHistory`; `OfficialControlPlusDbContext` sigue reservado para la operación normal. Un parser único selecciona servidor normal, healthcheck, preflight o migración antes de construir configuración o servidor HTTP. Solo admite cero argumentos, `--health-check`, `--migrate-to <MigrationId>` o `--preflight-to <MigrationId>`. El identificador tiene longitud de 1 a 200 y solo acepta ASCII alfanumérico o `_`; nunca puede estar vacío ni comenzar por `--`. Cualquier otra forma devuelve código 2 y `Command line rejected` sin conexión ni escucha. Los dos modos de base de datos exigen que el entorno contenga exactamente `CONTROLPLUS_MIGRATIONS_ENABLED=true`.

`--preflight-to 20260913020000_CashRegisterModuleV1` admite exclusivamente ese objetivo. Abre mediante Npgsql una transacción `REPEATABLE READ`, la establece `READ ONLY` antes de leer y confirma `transaction_read_only=on` al inicio y al final. Un manifiesto tipado compartido define las dos tablas, la columna, los seis índices con tabla/columnas, las nueve funciones con firma/retorno, los once triggers con tabla y las siete restricciones base con nombre, tabla, tipo, validación y columnas. El runner verifica además la cadena EF exacta, las cuatro filas anteriores de `__EFMigrationsHistory`, objetivo pendiente y ausencia de turnos. Las consultas son estáticas y parametrizadas, no usan `psql`, shell, SQL dinámico, agregaciones/comparaciones de arrays ni concatenación de tipos PostgreSQL.

El comando futuro de preflight desde Compose es:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --preflight-to 20260913020000_CashRegisterModuleV1
```

El código `0` certifica todas las precondiciones, `2` indica que al menos una no se cumple o que gate/objetivo son inválidos y `1` indica un fallo técnico sanitizado. El preflight no aplica migraciones, no crea respaldos, no modifica datos, no hace commit y no intenta rollback de esquema.

El runner rechaza destinos vacíos, inexistentes, ya aplicados o anteriores al último aplicado. También rechaza historiales que no sean un prefijo exacto del catálogo y secuencias pendientes incoherentes. La única operación permitida es aplicar la secuencia ascendente pendiente cuyo último elemento sea exactamente el destino solicitado. No existe una opción para aplicar genéricamente todo lo pendiente.

El `ENTRYPOINT` de la imagen es `dotnet ControlPlus.Api.dll`; por eso Compose adjunta los argumentos escritos después de `api`. Tras certificación, respaldo y autorización, el comando futuro de aplicación para el objetivo de Caja es:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --migrate-to 20260913020000_CashRegisterModuleV1
```

`--no-deps` impide que este comando cree o reinicie PostgreSQL; el destino autorizado debe estar disponible previamente mediante la configuración normal de Compose. El runner conserva la cadena de conexión solo en memoria y nunca registra configuración, credenciales, tokens, JWT, Master Key ni contenido de `.env`. Solo informa el identificador objetivo, resultado y mensajes sanitizados. En caso de fallo devuelve un código distinto de cero y no intenta una reversión manual.

Procedimiento obligatorio para una aplicación persistente futura:

1. Obtener autorización explícita para el entorno y la migración; producción nunca se migra por iniciativa del operador ni mediante el arranque de la API.
2. Abrir una ventana de mantenimiento, crear un respaldo fuera del repositorio y comprobar que pueda restaurarse.
3. Confirmar de forma independiente la imagen que se ejecutará, el entorno destino, el estado actual del historial y el identificador exacto que cerrará la secuencia pendiente.
4. Ejecutar una sola vez el comando controlado y exigir código de salida 0. No reintentarlo a ciegas.
5. Verificar por un mecanismo autorizado que `__EFMigrationsHistory` contiene exactamente una fila para el objetivo, comprobar `/api/health` y `/api/health/database`, y revisar los errores sanitizados.
6. Conservar el respaldo hasta terminar la verificación funcional. Cualquier reversión requiere un plan y una autorización separados.

La API normal no llama `Migrate`, `MigrateAsync`, `EnsureCreated` ni equivalentes. No aplique la migración base sobre un esquema existente: el SQL oficial está diseñado para una base vacía. No use operaciones que borren volúmenes.

## Prueba integrada

`OfficialSchemaIntegrationTests` inicia PostgreSQL 17 con Testcontainers, aplica la cadena completa y verifica las 61 tablas resultantes, sus PK/FK restrictivas, catálogos, importes `numeric(18,0)`, cantidades enteras y concurrencia entre EF Core y PostgreSQL. Las pruebas específicas de Caja ejercitan transición, reversión, inmutabilidad, consumo de autorizaciones y rollback.

`ControlledMigrationTests` ejecuta el DLL real contra PostgreSQL 17 efímero y demuestra gates, destino obligatorio, rechazo de destinos inexistentes/aplicados/reversos, aplicación exacta y única, ausencia de servidor HTTP, no migración durante el arranque normal y sanitización de secretos. `ControlledPreflightTests` recorre individualmente todos los elementos del manifiesto, elimina cada restricción base y comprueba también tipo, columnas y estado de validación incorrectos. `ControlledMigrationCommandLineTests` ejecuta el DLL real sin cadena de conexión para toda la matriz rechazada y confirma código 2, ausencia de conexión y ausencia de escucha HTTP.

```powershell
dotnet test ControlPlus.Infrastructure.Tests
```

La prueba elimina su contenedor aislado al finalizar y nunca usa la base persistente de desarrollo.
