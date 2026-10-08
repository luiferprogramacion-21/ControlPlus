# ControlPlus Backend

Backend del sistema POS ControlPlus. La fuente de verdad del diseño es el paquete oficial de Fase 4 ubicado en `../../docs/ControlPlus_Fase4_Documento_y_Complementos.zip`, dentro de la carpeta `docs` de la raíz del repositorio; no se modifica desde el código de la aplicación.

## Requisitos

- .NET SDK 10.
- Docker Desktop con contenedores Linux.
- Una configuración local de secretos fuera del control de versiones.

## Configuración local

Copie `.env.example` a `.env` únicamente en su entorno local y complete los valores requeridos sin versionar el archivo. La configuración incluye datos de PostgreSQL, `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_SIGNING_KEY`, duración y desfase del token, y `CONTROLPLUS_MASTER_KEY`.

`JWT_SIGNING_KEY` debe ser un secreto aleatorio de al menos 64 bytes para HS512. `CONTROLPLUS_MASTER_KEY` debe ser un secreto aleatorio de al menos 32 bytes UTF-8. No reutilice datos reales en archivos `.http`, pruebas o documentación.

## Ejecutar con Docker Compose

Desde esta carpeta:

```powershell
docker compose config --quiet
docker compose up --build
```

La API queda disponible en `http://localhost:8080`. Compruebe su estado mediante:

```powershell
Invoke-WebRequest http://localhost:8080/api/health
Invoke-WebRequest http://localhost:8080/api/health/database
```

Compose mantiene PostgreSQL en el volumen `controlplus_postgres_data`; no use `docker compose down -v` para un entorno con datos que deba conservarse. La API tiene un healthcheck que ejecuta su propio comando `dotnet` contra `/api/health`.

## Migraciones

La API no aplica migraciones al iniciar. La línea de comandos acepta exclusivamente: cero argumentos para el servidor normal, `--health-check`, `--migrate-to <MigrationId>`, `--preflight-to <MigrationId>`, el postflight de Clientes con `--postflight-to 20261005010000_CustomerSearchIndexesV1`, o el postflight de Inventario con `--postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 --expect-unrelated-reasons-sha256 <64-hex-sha256>`. `MigrationId` debe tener entre 1 y 200 caracteres y contener únicamente letras o dígitos ASCII y `_`; la huella debe contener exactamente 64 caracteres hexadecimales ASCII. Cualquier opción, argumento, identificador o huella inválidos, sintaxis con `=` o combinación adicional devuelve código 2 con `Command line rejected` antes de construir configuración, abrir una conexión o iniciar HTTP. Los tres modos controlados de base de datos exigen además el gate exacto `CONTROLPLUS_MIGRATIONS_ENABLED=true`.

Antes de autorizar la migración de Caja, el preflight futuro se ejecuta así:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --preflight-to 20260913020000_CashRegisterModuleV1
```

El preflight no aplica migraciones ni inicia HTTP. Usa `ControlPlusDbContext` y Npgsql dentro de una transacción PostgreSQL `REPEATABLE READ, READ ONLY`; certifica con código `0`, devuelve `2` si una precondición no se cumple y `1` ante un fallo técnico. Para Caja valida la cadena exacta de cuatro migraciones anteriores, el objetivo pendiente, ausencia de turnos y de estructura parcial, y presencia exacta de las siete restricciones base.

Solo después de un preflight exitoso, respaldo verificado y autorización independiente, el comando de aplicación futura es:

```powershell
docker compose run --rm --no-deps `
  -e CONTROLPLUS_MIGRATIONS_ENABLED=true `
  api --migrate-to 20260913020000_CashRegisterModuleV1
```

El contenedor temporal de aplicación valida que el historial sea un prefijo coherente, que el destino exista, esté pendiente y solo requiera una secuencia ascendente que termine exactamente en él. No inicia HTTP ni acepta “todo lo pendiente”; un error termina con código distinto de cero y sin rollback manual automático. Las migraciones publicadas son inmutables.

`20260930010000_SeedInventoryAdjustmentReasonsV1` está publicada en el código, pero continúa pendiente de aplicación en la base PostgreSQL persistente local. Sus tres comandos futuros, que no deben ejecutarse sin evaluación, respaldo verificado y autorización específica, son:

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

El preflight de Inventario exige exactamente las cinco predecesoras terminando en Caja, objetivo pendiente, las once columnas de `configuracion.motivo_operacion`, el índice natural correcto y ausencia total de los cuatro motivos aprobados y de colisiones UUID. También emite `motivos_ajenos` y una huella canónica SHA-256 de las once columnas de todos los motivos ajenos, sin imprimir sus valores. Esa huella se captura literalmente antes de migrar y se suministra al postflight. La migración acepta únicamente el estado con Caja como quinta y última fila aplicada, por lo que nunca aplica Caja e Inventario juntos. El postflight es de solo lectura, exige historial `6/6/0`, valida `4×E` motivos completos y rechaza si la huella ajena cambió. Preflight y postflight confirman `READ ONLY` al inicio y al final y terminan con rollback; ninguno usa `IMigrator`, DDL, HTTP ni escritura.

La comparación SHA-256 detecta adiciones, eliminaciones o modificaciones entre ambas comprobaciones, pero no es una garantía frente a escrituras simultáneas fuera de ellas. El flujo persistente futuro sigue exigiendo una ventana de mantenimiento que impida cambios externos desde el preflight hasta terminar el postflight.

`20261005010000_CustomerSearchIndexesV1` también está publicada en código y pendiente de aplicación persistente. Solo agrega cuatro índices de búsqueda de Clientes y requiere que Inventario sea exactamente la sexta migración ya aplicada. Sus comandos futuros, documentados pero no ejecutados en este corte, son:

```bash
CONTROLPLUS_MIGRATIONS_ENABLED=true dotnet ControlPlus.Api.dll --preflight-to 20261005010000_CustomerSearchIndexesV1
CONTROLPLUS_MIGRATIONS_ENABLED=true dotnet ControlPlus.Api.dll --migrate-to 20261005010000_CustomerSearchIndexesV1
CONTROLPLUS_MIGRATIONS_ENABLED=true dotnet ControlPlus.Api.dll --postflight-to 20261005010000_CustomerSearchIndexesV1
```

Preflight exige las seis predecesoras, la forma oficial de `catalogo.cliente` y ausencia de los índices reservados. Postflight exige historial `7/7/0` y las cuatro definiciones exactas. Ambos son `REPEATABLE READ, READ ONLY`, terminan con rollback y no inician HTTP. Estos comandos requieren autorización y respaldo independientes; la publicación del código no afirma que los índices existan en la base persistente local.

Antes de cualquier uso persistente se debe contar con autorización explícita, ventana de mantenimiento, respaldo verificado y confirmación independiente del identificador objetivo. El comando no debe automatizarse en el arranque ni ejecutarse contra producción sin autorización expresa. Después se debe confirmar el código 0, una sola fila del objetivo en `__EFMigrationsHistory`, salud de API y ausencia de errores sanitizados. Consulte [docs/database.md](docs/database.md).

## Pruebas

Docker Desktop debe estar iniciado para las pruebas de integración aisladas:

```powershell
dotnet build ControlPlus.Backend.slnx --no-restore -m:1 /p:BuildInParallel=false
dotnet test ControlPlus.Domain.Tests --no-restore -m:1 /p:BuildInParallel=false
dotnet test ControlPlus.Infrastructure.Tests --no-restore -m:1 /p:BuildInParallel=false
dotnet test ControlPlus.Api.Tests --no-restore -m:1 /p:BuildInParallel=false
```

Testcontainers crea PostgreSQL 17 aislado para las pruebas y no utiliza la base persistente de desarrollo. Los comandos se ejecutan en serie porque el empaquetado WSL validado puede fallar silenciosamente al compilar `.slnx` en paralelo con mensajes `MSB4276`, mientras la solución serial y los proyectos compilan sin errores ni advertencias. Consulte [docs/testing.md](docs/testing.md).

## API y seguridad

OpenAPI está disponible únicamente en Development y Testing en `/openapi/v1.json`. Los endpoints protegidos usan `Authorization: Bearer <token>`; salud, login, instalación inicial y recuperación inicial son los únicos flujos anónimos. Para solicitudes manuales, use `ControlPlus.Api/ControlPlus.Api.http` y reemplace sus marcadores localmente, sin guardar secretos.

Más detalles:

- [Arquitectura](docs/architecture.md)
- [API](docs/api.md)
- [Seguridad](docs/security.md)
- [Catálogo](docs/catalog.md)
- [Inventario](docs/inventory.md)
- [Clientes](docs/customers.md)
- [Caja](docs/cash.md)
- Caja conserva la diferencia oficial de efectivo y separa el total; su migración ya aplicada conserva métodos de pago al revertir. Inventario y los índices de búsqueda de Clientes están publicados en código y pendientes en PostgreSQL persistente local. La validación usa únicamente Testcontainers; no despliega servicios ni migra PostgreSQL persistente.
- [Base de datos](docs/database.md)
- [Decisión de persistencia y concurrencia](docs/decisions/0001-persistencia-concurrencia-y-migraciones.md)
